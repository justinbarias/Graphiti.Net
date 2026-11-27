// tests/llm_client/test_cache.py (port/RULES.md, R25): class TestLLMCache with the fixture `cache`, and the recording
// of what the cache stores (R23, gap J7). values/cache_reranker.json holds what port/tools/record_cache_reranker.py
// recorded from Python: the rows of the table after each `set`, so that a cache file of Python and one of the port
// hold the same rows.
// test_close_and_del calls __del__ by hand: __del__ has no member (R16), so only the second call of close() stays.
namespace Graphiti.Port.Evaluation;

using System.Text;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

public sealed class TestLLMCache : IDisposable
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private readonly string tmpPath = Path.Combine(Path.GetTempPath(), "graphiti-cache-" + Guid.NewGuid().ToString("N"));
    private readonly LLMCache cache;

    public TestLLMCache()
    {
        // The fixture `cache`: a cache in a temporary directory, closed after each test.
        cache = new LLMCache(Path.Combine(tmpPath, "test_cache"));
    }

    public void Dispose()
    {
        cache.Close();
        Directory.Delete(tmpPath, recursive: true);
    }

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "cache_reranker.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, object?> Dict(params (string Key, object? Value)[] items)
    {
        var d = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in items)
        {
            d[key] = value;
        }

        return d;
    }

    // `a == b` on two JSON values: the text of both, with the separators of Python.
    private static void AssertSame(OrderedDictionary<string, object?>? expected, OrderedDictionary<string, object?>? actual) =>
        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(actual));

    [Fact]
    public void test_get_missing_key_returns_none()
    {
        Assert.Null(cache.Get("nonexistent"));
    }

    [Fact]
    public void test_set_and_get()
    {
        var value = Dict(("content", "hello"), ("tokens", 42L));
        cache.Set("key1", value);
        AssertSame(value, cache.Get("key1"));
    }

    [Fact]
    public void test_set_overwrites_existing()
    {
        cache.Set("key1", Dict(("version", 1L)));
        cache.Set("key1", Dict(("version", 2L)));
        AssertSame(Dict(("version", 2L)), cache.Get("key1"));
    }

    [Fact]
    public void test_multiple_keys()
    {
        cache.Set("a", Dict(("val", 1L)));
        cache.Set("b", Dict(("val", 2L)));
        cache.Set("c", Dict(("val", 3L)));

        AssertSame(Dict(("val", 1L)), cache.Get("a"));
        AssertSame(Dict(("val", 2L)), cache.Get("b"));
        AssertSame(Dict(("val", 3L)), cache.Get("c"));
    }

    [Fact]
    public void test_complex_nested_value()
    {
        var value = Dict(
            ("choices", new List<object?> { Dict(("message", Dict(("role", "assistant"), ("content", "test")))) }),
            ("usage", Dict(("prompt_tokens", 10L), ("completion_tokens", 5L))),
            ("nested", Dict(("a", new List<object?> { 1L, 2L, 3L }), ("b", null), ("c", true))));
        cache.Set("complex", value);
        AssertSame(value, cache.Get("complex"));
    }

    [Fact]
    public void test_non_serializable_value_is_skipped()
    {
        // A lambda has no JSON form. The port uses an object with no JSON form.
        cache.Set("bad", Dict(("func", new object())));
        Assert.Null(cache.Get("bad"));
    }

    [Fact]
    public void test_corrupted_entry_returns_none()
    {
        // Directly insert invalid JSON into the database
        Execute(Path.Combine(tmpPath, "test_cache", "cache.db"), "not valid json{{{", "corrupt");
        Assert.Null(cache.Get("corrupt"));
    }

    [Fact]
    public void test_creates_directory()
    {
        var cacheDir = Path.Combine(tmpPath, "nested", "dir", "cache");
        var c = new LLMCache(cacheDir);
        try
        {
            Assert.True(Directory.Exists(cacheDir));
            Assert.True(File.Exists(Path.Combine(cacheDir, "cache.db")));
        }
        finally
        {
            c.Close();
        }
    }

    [Fact]
    public void test_persistence_across_instances()
    {
        var cacheDir = Path.Combine(tmpPath, "persist_cache");
        var c1 = new LLMCache(cacheDir);
        c1.Set("persist_key", Dict(("data", "survives")));
        c1.Close();

        var c2 = new LLMCache(cacheDir);
        try
        {
            AssertSame(Dict(("data", "survives")), c2.Get("persist_key"));
        }
        finally
        {
            c2.Close();
        }
    }

    [Fact]
    public void test_close_and_del()
    {
        var c = new LLMCache(Path.Combine(tmpPath, "close_test"));
        c.Close();

        // Python calls c.__del__(), which closes again and suppresses each error. The port has no member for it (R16),
        // so a second close() stands for it.
        c.Close();
    }

    // sqlite3.connect(timeout=5.0) sets a busy timeout of 5 seconds. A second connection holds the write lock and commits
    // after 300 milliseconds, and set() and get() wait for it and return with no error.
    [Fact]
    public async Task A_locked_database_is_waited_for_and_the_call_returns_when_the_lock_ends()
    {
        var db = Path.Combine(tmpPath, "test_cache", "cache.db");
        cache.Set("k", Dict(("v", 1L)));
        foreach (var begin in new[] { "BEGIN IMMEDIATE", "BEGIN EXCLUSIVE" })
        {
            using var holder = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
            holder.Open();
            using (var start = holder.CreateCommand())
            {
                start.CommandText = begin;
                start.ExecuteNonQuery();
            }

            var release = Task.Run(
                async () =>
                {
                    await Task.Delay(300, TestContext.Current.CancellationToken);
                    using var commit = holder.CreateCommand();
                    commit.CommandText = "COMMIT";
                    commit.ExecuteNonQuery();
                },
                TestContext.Current.CancellationToken);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (begin == "BEGIN IMMEDIATE")
            {
                cache.Set("k2", Dict(("v", 2L)));
            }
            else
            {
                AssertSame(Dict(("v", 1L)), cache.Get("k"));
            }

            watch.Stop();
            await release;
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(200), $"{begin}: returned after {watch.Elapsed}");
        }

        AssertSame(Dict(("v", 2L)), cache.Get("k2"));
    }

    // The SQL of the upstream test: INSERT OR REPLACE INTO cache (key, value) VALUES (?, ?). It goes through a second
    // connection, because the connection of LLMCache is private (`cache._conn` in Python).
    private static void Execute(string dbPath, string? value, string key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO cache (key, value) VALUES ($key, $value)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static (string Key, string Value, string Type)? Row(string dbPath, string key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value, typeof(value) FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    private static long Count(string dbPath, string key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return (long)command.ExecuteScalar()!;
    }

    private static long TotalRows(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM cache";
        return (long)command.ExecuteScalar()!;
    }

    private static List<object?> Items(object? value) => Py.AsList(value);

    private static OrderedDictionary<string, object?> Obj(object? value) => Py.AsDict(value);

    private static void AssertRow(object? expected, (string Key, string Value, string Type)? actual, string label)
    {
        if (expected is null)
        {
            Assert.True(actual is null, label);
            return;
        }

        var items = Items(expected);
        Assert.True(actual is not null, label);
        Assert.Equal((string)items[0]!, actual.Value.Key);
        Assert.Equal((string)items[1]!, actual.Value.Value);
        Assert.Equal((string)items[2]!, actual.Value.Type);
    }

    private static void AssertLog(object? expected, List<(LogLevel Level, string Message)> actual, string label)
    {
        var lines = Items(expected).Select(Items).ToList();
        Assert.True(lines.Count == actual.Count, label);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal((string)lines[i][0]!, actual[i].Level == LogLevel.Warning ? "WARNING" : actual[i].Level.ToString());
            Assert.Equal((string)lines[i][1]!, actual[i].Message);
        }
    }

    [Fact]
    public void The_table_and_each_stored_row_are_the_rows_that_Python_stores()
    {
        var recorded = Obj(Document["cache"]);
        var dir = Path.Combine(tmpPath, "recorded");
        var db = Path.Combine(dir, "cache.db");
        var c = new LLMCache(dir);
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()))
            {
                connection.Open();
                using var schema = connection.CreateCommand();
                schema.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'cache' ORDER BY type";
                using var reader = schema.ExecuteReader();
                var sql = new List<string>();
                while (reader.Read())
                {
                    sql.Add(reader.GetString(0));
                }

                Assert.Equal(Items(recorded["schema"]).Select(s => (string)s!).ToList(), sql);

                using var names = connection.CreateCommand();
                names.CommandText = "SELECT name FROM sqlite_master ORDER BY name";
                using var nameReader = names.ExecuteReader();
                var found = new List<string>();
                while (nameReader.Read())
                {
                    found.Add(nameReader.GetString(0));
                }

                Assert.Equal(Items(recorded["table_names"]).Select(s => (string)s!).ToList(), found);
            }

            var sets = Items(recorded["sets"]);
            Assert.True(sets.Count >= 11);
            foreach (var item in sets)
            {
                var set = Obj(item);
                var key = (string)set["key"]!;
                var value = Obj(set["value"]);
                var label = PyJson.Dumps(key);
                c.Set(key, value);
                AssertRow(set["row"], Row(db, key), label);
                Assert.Equal((string)set["get"]!, PyJson.Dumps(c.Get(key)));
            }

            foreach (var item in Items(recorded["overwrites"]))
            {
                var over = Obj(item);
                var key = (string)over["key"]!;
                var label = PyJson.Dumps(key);
                c.Set(key, Obj(over["first"]));
                AssertRow(over["before"], Row(db, key), label);
                c.Set(key, Obj(over["second"]));
                AssertRow(over["after"], Row(db, key), label);
                Assert.Equal(Py.AsLong(over["count"]), Count(db, key));
                Assert.Equal((string)over["get"]!, PyJson.Dumps(c.Get(key)));
            }

            foreach (var item in Items(recorded["missing"]))
            {
                var missing = Obj(item);
                var capture = new CaptureProvider();
                using var factory = new CaptureFactory(capture);
                using var scope = PortServices.Use(loggerFactory: factory);
                Assert.Equal((string)missing["get"]!, PyJson.Dumps(c.Get((string)missing["key"]!)));
                AssertLog(missing["log"], capture.Lines, PyJson.Dumps(missing["key"]));
            }

            foreach (var item in Items(recorded["bad"]))
            {
                var bad = Obj(item);
                var key = (string)bad["key"]!;
                var tag = (string)bad["tag"]!;
                var capture = new CaptureProvider();
                using var factory = new CaptureFactory(capture);
                using var scope = PortServices.Use(loggerFactory: factory);
                Assert.Null(bad["error"]);
                AssertRow(bad["row_before"], Row(db, key), tag);
                object? unserializable = tag == "bytes" ? new byte[] { 98, 121, 116, 101, 115 } : new object();
                c.Set(key, Dict((tag, unserializable)));
                AssertLog(bad["log"], capture.Lines, tag);
                AssertRow(bad["row_after"], Row(db, key), tag);
                Assert.Equal((string)bad["get"]!, PyJson.Dumps(c.Get(key)));
            }

            foreach (var item in Items(recorded["corrupt"]))
            {
                var corrupt = Obj(item);
                var key = (string)corrupt["key"]!;
                var raw = (string)corrupt["raw"]!;
                Execute(db, raw, key);
                var capture = new CaptureProvider();
                using var factory = new CaptureFactory(capture);
                using var scope = PortServices.Use(loggerFactory: factory);
                var label = PyJson.Dumps(raw);
                Assert.Null(corrupt["error"]);

                // The branch comes from what Python returned for the row.
                var expected = PyJson.Loads((string)corrupt["get"]!);
                switch (expected)
                {
                    case OrderedDictionary<string, object?>:
                        Assert.Equal((string)corrupt["get"]!, PyJson.Dumps(c.Get(key)));
                        break;
                    case null:
                        Assert.Null(c.Get(key));
                        break;
                    default:
                        // PORT: Python returns the decoded value of any JSON type. The typed result of the port is a dict
                        // or null, so the cast of the other types throws InvalidCastException.
                        Assert.Throws<InvalidCastException>(() => c.Get(key));
                        break;
                }

                AssertLog(corrupt["log"], capture.Lines, label);
            }

            // A key with a lone surrogate: Python raises UnicodeEncodeError for get and for set, and stores no row.
            foreach (var item in Items(recorded["surrogate_keys"]))
            {
                var surrogate = Obj(item);
                var key = (string)surrogate["key"]!;
                var call = (string)surrogate["call"]!;
                var capture = new CaptureProvider();
                using var factory = new CaptureFactory(capture);
                using var scope = PortServices.Use(loggerFactory: factory);
                var label = $"{PyJson.Dumps(key)} {call}";
                Assert.Equal(Py.AsLong(surrogate["rows_before"]), TotalRows(db));
                var error = (string?)surrogate["error"];
                Action action = call switch
                {
                    "get" => () => c.Get(key),
                    "set" => () => c.Set(key, Dict(("a", 1L))),
                    _ => () => c.Set(key, Dict(("s", new object()))),
                };
                if (error is null)
                {
                    action();
                }
                else
                {
                    Assert.Equal("UnicodeEncodeError", error);
                    Assert.True(Record.Exception(action) is EncoderFallbackException, label);
                }

                AssertLog(surrogate["log"], capture.Lines, label);
                Assert.True(Py.AsLong(surrogate["rows_after"]) == TotalRows(db), label);
            }

            // A NULL value: json.loads(None) raises TypeError.
            Assert.Equal("TypeError", (string)recorded["null_value_error"]!);
            Execute(db, null, "nullvalue");
            Assert.Throws<InvalidCastException>(() => c.Get("nullvalue"));

            c.Close();
            c.Close();
            Assert.Equal("ProgrammingError", (string)recorded["get_after_close"]!);
            Assert.Throws<InvalidOperationException>(() => c.Get("k"));
        }
        finally
        {
            c.Close();
        }

        var creates = Items(recorded["creates"]);
        Assert.Equal([true, true], creates);
        Assert.Equal("""{"data": "survives"}""", (string)recorded["persist"]!);
    }

    private sealed class CaptureFactory(CaptureProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider1)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        internal List<(LogLevel Level, string Message)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(LogLevel, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
