// What the ported upstream tests of the Graphiti class share (plan 0004, layer 9, packet P9f): tests/helpers_test.py and
// the fixtures of the four upstream files (the mock model, the mock embedder, the mock cross-encoder, and the driver
// fixture of conftest.py), as fakes at the network boundary (AGENTS.md, rule 7), plus a graph fixture for the real Neo4j.
//
// PORT: the driver fixture of conftest.py clears the groups `graphiti_test_group` and `graphiti_test_group_2` before each
// test. These tests share one Neo4j with other tests and never clear the whole graph. Each test gets two group ids of its
// own (the prefix `p9f_`), filters each read by them, and deletes only the data of these two groups at the end.
namespace Graphiti.E2E.Tests;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.Neo4j;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

/// <summary>The time helpers of the tests: <c>datetime.now()</c> (a naive value) and <c>now + timedelta(days=n)</c>.</summary>
internal static class PyClock
{
    /// <summary><c>datetime.now()</c>: the naive local time.</summary>
    internal static PyDateTime Now() => From(DateTime.Now);

    /// <summary><c>value + timedelta(days=days)</c> for a value of the clock fields.</summary>
    internal static PyDateTime Plus(PyDateTime value, int days)
    {
        var moved = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second)
            .AddTicks(value.Microsecond * 10L)
            .AddDays(days);
        return value.OffsetMicroseconds is null
            ? From(moved)
            : throw new InvalidOperationException("the tests use naive values");
    }

    /// <summary><c>datetime(2026, 1, 1, tzinfo=timezone.utc)</c>.</summary>
    internal static PyDateTime Utc2026 { get; } = new(2026, 1, 1, offsetMicroseconds: 0);

    private static PyDateTime From(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, (int)(value.Ticks % TimeSpan.TicksPerSecond / 10));
}

/// <summary>
/// The embeddings of <c>tests/helpers_test.py</c>: one fixed vector of 384 numbers in [0, 0.9) for each of the keys of
/// the upstream table. Python draws them with <c>np.random.uniform</c> when the module loads. Here a generator seeded with
/// the key gives the same vector in each run, and the cosine score of two keys stays low, as it does in Python.
/// </summary>
internal static class MockEmbeddings
{
    internal const int Dimension = 384;

    internal static readonly string[] Keys =
    [
        "Alice", "Bob", "Charlie", "Alice likes Bob", "Alice knows Bob", "Alice knows Charlie", "Alice works with Bob",
        "Alice manages Bob", "test_entity_1", "test_entity_2", "test_entity_3", "test_entity_4", "test_entity_alice",
        "test_entity_bob", "test_entity_1 is a duplicate of test_entity_2", "test_entity_3 is a duplicate of test_entity_4",
        "test_entity_1 relates to test_entity_2", "test_entity_1 relates to test_entity_3", "test_entity_2 relates to test_entity_3",
        "test_entity_1 relates to test_entity_4", "test_entity_2 relates to test_entity_4", "test_entity_3 relates to test_entity_4",
        "test_community_1", "test_community_2",
    ];

    private static readonly Dictionary<string, List<double>> Table = Build();

    internal static List<double> For(string key) =>
        Table.TryGetValue(key, out var vector) ? [.. vector] : throw new KeyNotFoundException($"'{key}'");

    private static Dictionary<string, List<double>> Build()
    {
        var table = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            var seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0);
            var random = new Random(seed);
            table[key] = [.. Enumerable.Range(0, Dimension).Select(_ => random.NextDouble() * 0.9)];
        }

        table["Alice Smith"] = table["Alice"];
        return table;
    }
}

/// <summary>
/// The <c>mock_embedder</c> fixture: <c>Mock(spec=EmbedderClient)</c> whose <c>create</c> answers from the table by the
/// text (a list is joined with a space) and raises <c>KeyError</c> for another text.
/// PORT: <c>create_batch</c> of that mock is an AsyncMock whose result is empty when it is iterated, so a batch of one or
/// more texts answers with an empty list. <see cref="BatchLikeCreate"/> true answers each text of the batch from the
/// table instead. The upstream tests of <c>add_triplet</c> need this: with the mock as it is, they fail on Python 0.32.0.
/// </summary>
internal sealed class MockEmbedder(bool batchLikeCreate = false) : EmbedderClient
{
    private int creates;

    internal bool BatchLikeCreate { get; } = batchLikeCreate;

    internal int Creates => Volatile.Read(ref creates);

    internal ConcurrentQueue<List<string>> Batches { get; } = new();

    internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref creates);
        return MockEmbeddings.For(string.Join(' ', inputData));
    }

    internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Batches.Enqueue([.. inputDataList]);
        return BatchLikeCreate ? [.. inputDataList.Select(text => MockEmbeddings.For(text))] : [];
    }
}

/// <summary>
/// The <c>mock_llm_client</c> fixture. Python gives <c>Mock(spec=LLMClient)</c> a <c>generate_response</c> that a test then
/// replaces with an AsyncMock that returns each of a list of values in turn (or raises one that is an exception). A call
/// with no value left, and a call of the default Mock (it returns a dict, and a dict cannot be awaited), raises.
/// </summary>
internal sealed class MockLlmClient : LLMClient
{
    private readonly ConcurrentQueue<object> replies = new();

    private int awaited;

    internal MockLlmClient(params object[] replies)
        : base(null, false)
    {
        foreach (var reply in replies)
        {
            this.replies.Enqueue(reply);
        }
    }

    internal override string PythonName => "Mock";

    /// <summary>
    /// Gets the answer of each call, by the name of the response model and the messages. A call then reads no queued reply.
    /// The add_triplet tests give <c>{'duplicate_facts': [], 'invalidate_facts': []}</c> for each call, as upstream.
    /// </summary>
    internal Func<List<Message>, IResponseModel?, OrderedDictionary<string, object?>>? Script { get; init; }

    /// <summary>Gets each call: the messages of the first argument.</summary>
    internal ConcurrentQueue<List<Message>> Calls { get; } = new();

    internal int AwaitCount => Volatile.Read(ref awaited);

    /// <summary>The text of the messages of one call, as <c>'\n'.join(message.content ...)</c>.</summary>
    internal static string Text(List<Message> messages) => string.Join('\n', messages.Select(m => m.Content));

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

    internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Enqueue(messages);
        Interlocked.Increment(ref awaited);
        if (Script is not null)
        {
            return Script(messages, responseModel);
        }

        if (!replies.TryDequeue(out var reply))
        {
            throw new InvalidOperationException("the mock model has no reply left for this call");
        }

        return reply switch
        {
            Exception error => throw error,
            OrderedDictionary<string, object?> value => value,
            _ => throw new InvalidOperationException("a reply is a dict or an exception"),
        };
    }
}

/// <summary>The <c>mock_cross_encoder_client</c> fixture. The upstream mock returns a dict from a plain Mock, so no test awaits it.</summary>
internal sealed class MockCrossEncoder : CrossEncoderClient
{
    internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("the mock cross-encoder is not awaited by these tests");
}

/// <summary>
/// The graph fixture: a driver for the shared Neo4j and two groups of the test. <c>graph_driver</c> of conftest.py is
/// parametrized for each provider. Only Neo4j is ported (decision D10).
/// </summary>
internal sealed class GraphFixture : IAsyncDisposable
{
    internal const string GroupPrefix = "p9f_";

    internal GraphFixture()
    {
        Driver = new Neo4jDriver(Neo4jSidecar.Uri, Neo4jSidecar.User, Neo4jSidecar.Password);
    }

    internal Neo4jDriver Driver { get; }

    /// <summary>Gets <c>group_id</c> of <c>helpers_test.py</c>.</summary>
    internal string GroupId { get; } = $"{GroupPrefix}{Guid.NewGuid():N}";

    /// <summary>Gets <c>group_id_2</c> of <c>helpers_test.py</c>.</summary>
    internal string GroupId2 { get; } = $"{GroupPrefix}{Guid.NewGuid():N}";

    internal List<string> Groups => [GroupId, GroupId2];

    public async ValueTask DisposeAsync()
    {
        try
        {
            await GraphDataOperations.ClearDataAsync(Driver, Groups, CancellationToken.None);
        }
        finally
        {
            await Driver.CloseAsync(CancellationToken.None);
        }
    }

    /// <summary>A <c>Graphiti(graph_driver=..., llm_client=..., embedder=..., cross_encoder=...)</c> on the fixture driver.</summary>
    internal GraphitiCore Graphiti(MockLlmClient llm, EmbedderClient embedder, CrossEncoderClient? crossEncoder = null) =>
        new(graphDriver: Driver, llmClient: llm, embedder: embedder, crossEncoder: crossEncoder ?? new MockCrossEncoder());

    /// <summary><c>get_node_count</c> of <c>helpers_test.py</c>.</summary>
    internal async Task<long> GetNodeCountAsync(List<string> uuids)
    {
        var (results, _, _) = await Driver.ExecuteQueryAsync(
            "MATCH (n) WHERE n.uuid IN $uuids RETURN COUNT(n) as count",
            new() { ["uuids"] = uuids });
        return Convert.ToInt64(results[0]["count"], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary><c>get_edge_count</c> of <c>helpers_test.py</c>.</summary>
    internal async Task<long> GetEdgeCountAsync(List<string> uuids)
    {
        var (results, _, _) = await Driver.ExecuteQueryAsync(
            """
            MATCH (n)-[e]->(m)
            WHERE e.uuid IN $uuids
            RETURN COUNT(e) as count
            UNION ALL
            MATCH (e:RelatesToNode_)
            WHERE e.uuid IN $uuids
            RETURN COUNT(e) as count
            """,
            new() { ["uuids"] = uuids });
        return results.Sum(result => Convert.ToInt64(result["count"], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary><c>assert_datetimes_equal</c>: both values read as UTC.</summary>
    internal static void AssertDatetimesEqual(PyDateTime? left, PyDateTime? right) =>
        Assert.Equal(DatetimeUtils.EnsureUtc(left), DatetimeUtils.EnsureUtc(right));

    /// <summary><c>assert_episodic_node_equals</c>.</summary>
    internal void AssertEpisodicNodeEquals(EpisodicNode retrieved, EpisodicNode sample)
    {
        Assert.Equal(sample.Uuid, retrieved.Uuid);
        Assert.Equal(sample.Name, retrieved.Name);
        Assert.Equal(GroupId, retrieved.GroupId);
        AssertDatetimesEqual(retrieved.CreatedAt, sample.CreatedAt);
        Assert.Equal(sample.Source, retrieved.Source);
        Assert.Equal(sample.SourceDescription, retrieved.SourceDescription);
        Assert.Equal(sample.Content, retrieved.Content);
        AssertDatetimesEqual(retrieved.ValidAt, sample.ValidAt);
        Assert.Equal(sample.EntityEdges.Order(StringComparer.Ordinal), retrieved.EntityEdges.Order(StringComparer.Ordinal));
    }

    /// <summary><c>assert_entity_node_equals</c>.</summary>
    internal async Task AssertEntityNodeEqualsAsync(EntityNode retrieved, EntityNode sample)
    {
        await retrieved.LoadNameEmbeddingAsync(Driver);
        Assert.Equal(sample.Uuid, retrieved.Uuid);
        Assert.Equal(sample.Name, retrieved.Name);
        Assert.Equal(sample.GroupId, retrieved.GroupId);
        Assert.Equal(sample.Labels.Order(StringComparer.Ordinal), retrieved.Labels.Order(StringComparer.Ordinal));
        AssertDatetimesEqual(retrieved.CreatedAt, sample.CreatedAt);
        Assert.NotNull(retrieved.NameEmbedding);
        Assert.NotNull(sample.NameEmbedding);
        AssertAllClose(retrieved.NameEmbedding, sample.NameEmbedding);
        Assert.Equal(sample.Summary, retrieved.Summary);
        Assert.Equal(sample.Attributes.Count, retrieved.Attributes.Count);
        foreach (var (key, value) in sample.Attributes)
        {
            Assert.Equal(value, retrieved.Attributes[key]);
        }
    }

    /// <summary><c>assert_episodic_edge_equals</c>.</summary>
    internal static void AssertEpisodicEdgeEquals(EpisodicEdge retrieved, EpisodicEdge sample)
    {
        Assert.Equal(sample.Uuid, retrieved.Uuid);
        Assert.Equal(sample.GroupId, retrieved.GroupId);
        AssertDatetimesEqual(retrieved.CreatedAt, sample.CreatedAt);
        Assert.Equal(sample.SourceNodeUuid, retrieved.SourceNodeUuid);
        Assert.Equal(sample.TargetNodeUuid, retrieved.TargetNodeUuid);
    }

    /// <summary><c>assert_entity_edge_equals</c>.</summary>
    internal async Task AssertEntityEdgeEqualsAsync(EntityEdge retrieved, EntityEdge sample)
    {
        await retrieved.LoadFactEmbeddingAsync(Driver);
        Assert.Equal(sample.Uuid, retrieved.Uuid);
        Assert.Equal(sample.GroupId, retrieved.GroupId);
        AssertDatetimesEqual(retrieved.CreatedAt, sample.CreatedAt);
        Assert.Equal(sample.SourceNodeUuid, retrieved.SourceNodeUuid);
        Assert.Equal(sample.TargetNodeUuid, retrieved.TargetNodeUuid);
        Assert.Equal(sample.Name, retrieved.Name);
        Assert.Equal(sample.Fact, retrieved.Fact);
        Assert.NotNull(retrieved.FactEmbedding);
        Assert.NotNull(sample.FactEmbedding);
        AssertAllClose(retrieved.FactEmbedding, sample.FactEmbedding);
        Assert.Equal(sample.Episodes, retrieved.Episodes);
        AssertDatetimesEqual(retrieved.ExpiredAt, sample.ExpiredAt);
        AssertDatetimesEqual(retrieved.ValidAt, sample.ValidAt);
        AssertDatetimesEqual(retrieved.InvalidAt, sample.InvalidAt);
        Assert.Equal(sample.Attributes.Count, retrieved.Attributes.Count);
    }

    /// <summary><c>np.allclose</c> with the default tolerances (rtol 1e-5, atol 1e-8).</summary>
    internal static void AssertAllClose(IReadOnlyList<double> actual, IReadOnlyList<double> expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(
                Math.Abs(actual[i] - expected[i]) <= 1e-8 + (1e-5 * Math.Abs(expected[i])),
                $"element {i}: {actual[i]} is not close to {expected[i]}");
        }
    }
}

/// <summary>A log factory that keeps each line of each category with its level.</summary>
internal sealed class AllLogsFactory(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Capture(lines);

    public void Dispose()
    {
    }

    private sealed class Capture(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue((logLevel, formatter(state, exception)));
    }
}

/// <summary>A hand-built group: one attribute for each function. A null value is an attribute that is not callable.</summary>
internal sealed class SimpleGroup(Dictionary<string, ChatPromptFunction?> functions) : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        var found = functions.TryGetValue(name, out var value);
        function = value;
        return found;
    }
}

/// <summary>A hand-built library, the <c>SimpleNamespace</c> of the upstream tests: one attribute for each group, and no specs.</summary>
internal sealed class SimpleLibrary(Dictionary<string, IPromptGroup?> groups) : IPromptLibrary
{
    public IReadOnlyDictionary<string, PromptSpec>? Specs => null;

    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        var found = groups.TryGetValue(name, out var value);
        group = value;
        return found;
    }

    /// <summary><c>library.group.function(context)</c>.</summary>
    internal static ChatPrompt Call(IPromptLibrary library, string group, string function, OrderedDictionary<string, object?>? context = null)
    {
        Assert.True(library.TryGetGroup(group, out var found));
        Assert.True(found!.TryGetFunction(function, out var builder));
        return builder!(context ?? []);
    }
}

/// <summary>
/// The <c>StubDriver</c> of <c>test_episode_group_isolation.py</c>: a driver at the network boundary. Each query records its
/// text and its parameters, and the answer is the same records each time.
/// </summary>
internal sealed class StubDriver(
    List<OrderedDictionary<string, object?>>? records = null,
    Func<string, OrderedDictionary<string, object?>, List<OrderedDictionary<string, object?>>>? route = null) : GraphDriver
{
    internal List<(string Query, OrderedDictionary<string, object?> Params)> Calls { get; } = [];

    /// <summary>Gets the count of calls of <c>session()</c>, which a write needs. The stub has no session.</summary>
    internal int SessionRequests { get; private set; }

    /// <summary>Gets or sets the driver that <c>clone(database)</c> returns. The default is the stub itself, as the Neo4j driver does.</summary>
    internal GraphDriver? CloneTo { get; set; }

    internal override string PythonName => "StubDriver";

    internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var copy = new OrderedDictionary<string, object?>(parameters);
        Calls.Add((cypherQuery, copy));
        var answer = route is null ? records ?? [] : route(cypherQuery, copy);
        return ([.. answer.Select(r => new OrderedDictionary<string, object?>(r))], null, null);
    }

    internal override GraphDriverSession Session(string? database = null)
    {
        SessionRequests++;
        return new StubSession(this);
    }

    /// <summary>A session that runs each query as <c>execute_query</c> of the stub, so the stub records it.</summary>
    private sealed class StubSession(StubDriver owner) : GraphDriverSession
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            owner.ExecuteQueryAsync(query, parameters, cancellationToken);

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            func(new StubTransaction(owner));

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            func(new StubTransaction(owner));
    }

    private sealed class StubTransaction(StubDriver owner) : Transaction
    {
        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            owner.ExecuteQueryAsync(query, parameters, cancellationToken);
    }

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    internal override GraphDriver Clone(string database) => CloneTo ?? this;
}
