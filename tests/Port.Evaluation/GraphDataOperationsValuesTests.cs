// Evaluation test for graphiti_core/utils/maintenance/graph_data_operations.py (port/RULES.md, R23, R25).
// The recording is the list async_cases of values/maintenance_helpers.json, which port/tools/record_maintenance_helpers.py
// wrote from Python. Both sides run clear_data and retrieve_episodes with a fake driver, a fake session, a fake
// transaction, and a fake graph operations interface. Each fake yields once and then records its call. The calls and
// the log lines are one ordered list.
namespace Graphiti.Port.Evaluation;

using System.Collections;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class GraphDataOperationsValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> Cases =
        MaintenanceHelpersValuesTests.List(MaintenanceHelpersValuesTests.Document["async_cases"]).Select(MaintenanceHelpersValuesTests.Dict).ToList();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void The_recording_reaches_each_branch()
    {
        var inputs = Cases.Select(c => MaintenanceHelpersValuesTests.Dict(c["input"])).ToList();
        foreach (var method in new[] { "clear_data", "retrieve_episodes" })
        {
            foreach (var ops in new[] { "none", "not_implemented", "returns" })
            {
                Assert.Contains(inputs, i => (string)i["method"]! == method && (string)i["ops"]! == ops);
            }
        }

        foreach (var provider in new[] { "neo4j", "falkordb", "neptune", "relate" })
        {
            Assert.Contains(inputs, i => (string)i["method"]! == "retrieve_episodes" && (string)i["provider"]! == provider && i.GetValueOrDefault("saga") is not null);
            Assert.Contains(inputs, i => (string)i["method"]! == "retrieve_episodes" && (string)i["provider"]! == provider && i.GetValueOrDefault("saga") is null && (string)i["ops"]! != "returns");
        }

        Assert.Contains(inputs, i => (string)i["method"]! == "clear_data" && i.TryGetValue("group_ids", out var g) && g is null);
        Assert.Contains(inputs, i => (string)i["method"]! == "clear_data" && i.TryGetValue("group_ids", out var g) && g is List<object?> { Count: > 1 });
        Assert.Contains(inputs, i => i.TryGetValue("source", out var s) && s is not null);
        Assert.Contains(inputs, i => i.TryGetValue("group_ids", out var g) && g is List<object?> { Count: > 0 } && (string)i["method"]! == "retrieve_episodes");
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = MaintenanceHelpersValuesTests.Dict(Cases[index]["input"]);
        var output = MaintenanceHelpersValuesTests.Dict(Cases[index]["output"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(input, source, failAt: input.TryGetValue("fail_at", out var failAt) ? (long)failAt! : -1, cancelAt: -1);
        var capture = new EventCapture(run.Events);
        using var scope = PortServices.Use(loggerFactory: capture);

        Exception? error = null;
        object? result = null;
        try
        {
            result = await LoopGuard.RunAsync(() => Call(run, source.Token));
        }
        catch (Exception e) when (e is InvalidOperationException or NotImplementedException or ArgumentException or KeyNotFoundException)
        {
            error = e;
        }

        var expectedError = output["error"] as OrderedDictionary<string, object?>;
        if (expectedError is null)
        {
            Assert.True(error is null, error?.ToString());
            Assert.Equal(PyJson.Dumps(output["result"]), PyJson.Dumps(result));
        }
        else
        {
            Assert.NotNull(error);
            var type = (string)expectedError["type"]!;
            Assert.Equal(
                type switch
                {
                    "RuntimeError" => typeof(InvalidOperationException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    "ValueError" => typeof(ArgumentException),
                    "KeyError" => typeof(KeyNotFoundException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError is the Python repr of the key, which is not a contract of the port.
            if (type != "KeyError")
            {
                Assert.Equal((string)expectedError["text"]!, error.Message);
            }
        }

        var expectedEvents = MaintenanceHelpersValuesTests.List(output["events"]);
        Assert.Equal(expectedEvents.Count, run.Events.Count);
        for (var i = 0; i < expectedEvents.Count; i++)
        {
            Assert.Equal(PyJson.Dumps(expectedEvents[i]), PyJson.Dumps(run.Events[i]));
        }

        // Each fake got the token of the call (R25).
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    // The scenarios of the cancellation tests, with the count of the calls that take the token. A session open and a
    // session close take no token.
    private static readonly Dictionary<string, (OrderedDictionary<string, object?> Input, int Calls)> Scenarios = new()
    {
        ["clear_data"] = (Scenario("clear_data", "none", null, false), 2),
        ["clear_data_with_groups"] = (Scenario("clear_data", "none", ["g", "h", "i", "j"], false), 5),
        ["clear_data_not_implemented"] = (Scenario("clear_data", "not_implemented", ["g"], false), 6),
        ["clear_data_returns"] = (Scenario("clear_data", "returns", null, false), 1),
        ["retrieve_episodes"] = (Scenario("retrieve_episodes", "none", ["g"], false), 1),
        ["retrieve_episodes_saga"] = (Scenario("retrieve_episodes", "none", ["g"], true), 1),
        ["retrieve_episodes_not_implemented"] = (Scenario("retrieve_episodes", "not_implemented", null, false), 2),
        ["retrieve_episodes_returns"] = (Scenario("retrieve_episodes", "returns", null, true), 1),
    };

    public static TheoryData<string> ScenarioNames() => [.. Scenarios.Keys];

    public static TheoryData<string, int> LaterCalls()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, (_, calls)) in Scenarios)
        {
            for (var k = 0; k < calls - 1; k++)
            {
                data.Add(name, k);
            }
        }

        return data;
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8)
    // and makes no call that takes the token.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new Run(Scenarios[name].Input, source, failAt: -1, cancelAt: -1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => Call(run, source.Token)));
        Assert.Equal(0, run.Count);
    }

    // A test with no Python original: the token is canceled while call k runs. Call k is the last call that a fake
    // records, and the method throws OperationCanceledException at the call that follows (R8).
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(string name, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(Scenarios[name].Input, source, failAt: -1, cancelAt: k);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => Call(run, source.Token)));
        Assert.Equal(k + 1, run.Count);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_declared_count_of_calls_is_the_count_of_the_method(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(Scenarios[name].Input, source, failAt: -1, cancelAt: -1);

        await LoopGuard.RunAsync(() => Call(run, source.Token));
        Assert.Equal(Scenarios[name].Calls, run.Count);
    }

    private static OrderedDictionary<string, object?> Scenario(string method, string ops, List<object?>? groupIds, bool saga)
    {
        var input = new OrderedDictionary<string, object?>
        {
            ["method"] = method,
            ["provider"] = "neo4j",
            ["ops"] = ops,
            ["group_ids"] = groupIds,
            ["reference_time"] = new OrderedDictionary<string, object?> { ["$dt"] = "2024-06-01T00:00:00+00:00" },
            ["records"] = new List<object?>(),
        };
        if (saga)
        {
            input["saga"] = "s";
        }

        return input;
    }

    private static async Task<object?> Call(Run run, CancellationToken token)
    {
        var input = run.Input;
        var driver = new FakeDriver(run);
        if ((string)input["method"]! == "clear_data")
        {
            if (input.ContainsKey("default"))
            {
                await GraphDataOperations.ClearDataAsync(driver, cancellationToken: token);
            }
            else
            {
                await GraphDataOperations.ClearDataAsync(driver, Strings(input["group_ids"]), token);
            }

            return null;
        }

        var referenceTime = PyDateTime.FromIsoFormat((string)MaintenanceHelpersValuesTests.Dict(input["reference_time"])["$dt"]!);
        var lastN = input.TryGetValue("last_n", out var n) ? (long)n! : GraphDataOperations.EpisodeWindowLen;
        var groupIds = input.TryGetValue("group_ids", out var g) ? Strings(g) : null;
        EpisodeType? episodeSource = input.TryGetValue("source", out var s) && s is not null ? PyEnum.FromValue<EpisodeType>(s) : null;
        var saga = input.TryGetValue("saga", out var sagaName) ? (string?)sagaName : null;
        var episodes = await GraphDataOperations.RetrieveEpisodesAsync(driver, referenceTime, lastN, groupIds, episodeSource, saga, token);
        return episodes.Select(e => (object?)e.ModelDump(json: true)).ToList();
    }

    private static List<string>? Strings(object? value) => value is null ? null : MaintenanceHelpersValuesTests.List(value).Select(MaintenanceHelpersValuesTests.Str).ToList();

    // A parameter value with its Python type, as encode() of the recorder writes it.
    private static OrderedDictionary<string, object?> Encode(object? value) => value switch
    {
        null => new() { ["type"] = "none" },
        bool b => new() { ["type"] = "bool", ["value"] = b },
        long l => new() { ["type"] = "int", ["value"] = l },
        string text => new() { ["type"] = "str", ["value"] = text },
        PyDateTime dt => new() { ["type"] = "datetime", ["value"] = dt.IsoFormat() },
        IEnumerable items => new() { ["type"] = "list", ["items"] = items.Cast<object?>().Select(item => (object?)Encode(item)).ToList() },
        _ => new() { ["type"] = value.GetType().Name },
    };

    private static OrderedDictionary<string, object?> CallEvent(string kind, string query, OrderedDictionary<string, object?> parameters) => new()
    {
        ["kind"] = kind,
        ["query"] = query,
        ["keys"] = parameters.Keys.Cast<object?>().ToList(),
        ["values"] = parameters.Values.Select(v => (object?)Encode(v)).ToList(),
    };

    private sealed class Run(OrderedDictionary<string, object?> input, CancellationTokenSource source, long failAt, long cancelAt)
    {
        internal OrderedDictionary<string, object?> Input { get; } = input;

        internal List<object?> Events { get; } = [];

        internal List<CancellationToken> Tokens { get; } = [];

        // The count of the calls that take the token, as the count of fail() of the recorder.
        internal long Count { get; private set; }

        // A call that takes the token: it yields first, then reads the token, then records itself.
        internal async Task EnterAsync(OrderedDictionary<string, object?> recorded, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            Tokens.Add(token);
            Events.Add(recorded);
            Count++;
            if (Count - 1 == cancelAt)
            {
                await source.CancelAsync();
            }

            if (Count - 1 == failAt)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }

    private sealed class FakeDriver : GraphDriver
    {
        private readonly Run run;

        internal FakeDriver(Run run)
        {
            this.run = run;
            Provider = PyEnum.FromValue<GraphProvider>(run.Input["provider"]);
            GraphOperationsInterface = (string)run.Input["ops"]! == "none" ? null : new FakeOps(run);
        }

        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync(CallEvent("execute_query", cypherQuery, parameters), cancellationToken);
            var records = MaintenanceHelpersValuesTests.List(Py.DeepCopy(run.Input["records"])).Select(MaintenanceHelpersValuesTests.Dict).ToList();
            return (records, null, null);
        }

        internal override GraphDriverSession Session(string? database = null)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "session_open" });
            return new FakeSession(run);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeSession(Run run) : GraphDriverSession
    {
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "session_close" });
        }

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override async Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync(new OrderedDictionary<string, object?> { ["kind"] = "execute_write" }, cancellationToken);
            await func(new FakeTransaction(run));
        }

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeTransaction(Run run) : Transaction
    {
        internal override async Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            await run.EnterAsync(CallEvent("tx_run", query, parameters), cancellationToken);
    }

    private sealed record FakeOps(Run Run) : GraphOperationsInterface
    {
        internal override async Task ClearDataAsync(GraphDriver driver, List<string>? groupIds = null, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(new OrderedDictionary<string, object?> { ["kind"] = "ops_clear_data", ["values"] = new List<object?> { Encode(groupIds) } }, cancellationToken);
            if ((string)Run.Input["ops"]! == "not_implemented")
            {
                throw new NotImplementedException(string.Empty);
            }
        }

        internal override async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
            GraphDriver driver,
            PyDateTime referenceTime,
            long lastN = 3,
            List<string>? groupIds = null,
            EpisodeType? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                new OrderedDictionary<string, object?>
                {
                    ["kind"] = "ops_retrieve_episodes",
                    ["values"] = new List<object?> { Encode(referenceTime), Encode(lastN), Encode(groupIds), Encode(source is { } s ? PyEnum.Name(s) : null), Encode(saga) },
                },
                cancellationToken);
            if ((string)Run.Input["ops"]! == "not_implemented")
            {
                throw new NotImplementedException(string.Empty);
            }

            var now = PyDateTime.FromIsoFormat((string)MaintenanceHelpersValuesTests.Document["fixed_now"]!);
            return
            [
                EpisodicNode.ModelValidate(new OrderedDictionary<string, object?>
                {
                    ["uuid"] = "ep-9",
                    ["name"] = "episode 9",
                    ["group_id"] = "g",
                    ["created_at"] = now,
                    ["source"] = EpisodeType.Text,
                    ["source_description"] = "chat",
                    ["content"] = "content 9 東京",
                    ["valid_at"] = now,
                    ["entity_edges"] = new List<object?>(),
                }),
            ];
        }
    }

    // A log line is one more event, in the order of the calls.
    private sealed class EventCapture(List<object?> events) : Microsoft.Extensions.Logging.ILoggerFactory
    {
        private readonly MaintenanceLogCapture inner = new();

        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider)
        {
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(inner.CreateLogger(categoryName), inner, events);

        public void Dispose()
        {
        }

        private sealed class Logger(Microsoft.Extensions.Logging.ILogger logger, MaintenanceLogCapture capture, List<object?> events) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
                var (level, message) = capture.Lines[^1];
                events.Add(new OrderedDictionary<string, object?> { ["kind"] = "log", ["level"] = level, ["message"] = message });
            }
        }
    }
}
