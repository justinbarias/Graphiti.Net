// Evaluation test for graphiti_core/utils/maintenance/combined_extraction.py (port/RULES.md, R23, R25).
// values/combined_extraction.json holds the calls that port/tools/record_combined_extraction.py recorded from Python:
// _episode_uuids_for_fact with generated inputs, and extract_nodes_and_edges with a fake LLM client. The calls and the
// log lines of each case are one ordered list.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

public class CombinedExtractionValuesTests
{
    private static readonly OrderedDictionary<string, object?> Values = CommunityCombinedValues.Load("combined_extraction.json");

    private static readonly PyDateTime Now = PyDateTime.FromIsoFormat((string)Values["fixed_now"]!);

    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(Values["async_cases"]).Select(Dict).ToList();

    private static readonly OrderedDictionary<string, GraphTypeDescriptor> Types = BuildTypes();

    // The scenarios of the cancellation tests: one recorded case with each shape of calls, and its count of fake calls.
    private static readonly Dictionary<string, (int Case, int Calls)> Scenarios = new()
    {
        ["one_group"] = (0, 2),
        ["three_groups_with_retries"] = (14, 6),
        ["two_groups_in_order"] = (15, 4),
    };

    public static TheoryData<int> AsyncCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

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

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        var constants = Dict(Values["constants"]);
        Assert.Equal(CombinedExtractionModule.TimestampBatchSize, (long)constants["TIMESTAMP_BATCH_SIZE"]!);
        Assert.Equal(CombinedExtractionModule.TimestampBatchAttempts, (long)constants["TIMESTAMP_BATCH_ATTEMPTS"]!);
    }

    [Fact]
    public void The_recording_reaches_each_branch()
    {
        var messages = AsyncCases
            .SelectMany(c => List(Dict(c["out"])["events"]).Select(Dict))
            .Where(e => (string)e["kind"]! == "log")
            .Select(e => (string)e["message"]!)
            .ToList();
        foreach (var text in new[]
        {
            "No entity types available", "No edge types available", "Excluding entity of type", "Skipping edge with empty fact",
            "Skipping edge with empty source", "not in strict edge ontology", "Skipping edge with excluded entity endpoint",
            "Failed to extract batch timestamps", "Batch timestamp count mismatch", "Dropping", "Materialized",
            "Combined extraction final",
        })
        {
            Assert.Contains(messages, m => m.Contains(text, StringComparison.Ordinal));
        }

        Assert.Contains(AsyncCases, c => Dict(c["out"]).ContainsKey("error"));
        Assert.Contains(AsyncCases, c => (long)Dict(c["out"])["max_in_flight"]! == 2);
    }

    [Fact]
    public void The_port_gives_each_recorded_result_of_episode_uuids_for_fact()
    {
        var cases = List(Values["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 80);
        foreach (var item in cases)
        {
            var input = Dict(item["in"]);
            var fact = CombinedFact.ModelValidate(Dict(input["fact"]));
            List<EpisodicNode> episodes = [.. List(input["episodes"]).Select(Episode)];
            var result = CombinedExtractionModule.EpisodeUuidsForFact(fact, episodes);
            Assert.Equal(PyJson.Dumps(Dict(item["out"])["ok"]), PyJson.Dumps(result.Select(x => (object?)x).ToList()));
        }
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(AsyncCaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = Dict(AsyncCases[index]["in"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new CommunityCombinedRun(input, source, cancelAt: -1);
        var output = new OrderedDictionary<string, object?>();
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
            }
            catch (Exception e) when (e is ValidationException or KeyNotFoundException or InvalidOperationException)
            {
                output["error"] = new OrderedDictionary<string, object?>
                {
                    ["type"] = CommunityCombinedValues.PythonErrorName(e),
                    ["text"] = e is ValidationException ? e.Message.Split('\n')[0] : e.Message,
                };
            }
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        var expected = Dict(AsyncCases[index]["out"]);
        var actual = Dict(Placeholders(output, []));
        Assert.Equal(PyJson.Dumps(expected.GetValueOrDefault("error")), PyJson.Dumps(actual.GetValueOrDefault("error")));
        var expectedEvents = List(expected["events"]);
        var actualEvents = List(actual["events"]);
        for (var i = 0; i < Math.Min(expectedEvents.Count, actualEvents.Count); i++)
        {
            Assert.Equal(PyJson.Dumps(expectedEvents[i]), PyJson.Dumps(actualEvents[i]));
        }

        Assert.Equal(expectedEvents.Count, actualEvents.Count);
        Assert.Equal(PyJson.Dumps(expected.GetValueOrDefault("ok")), PyJson.Dumps(actual.GetValueOrDefault("ok")));
        Assert.Equal(PyJson.Dumps(expected["max_in_flight"]), PyJson.Dumps(actual["max_in_flight"]));

        // Each fake got the token of the call (R25).
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_declared_count_of_calls_is_the_count_of_the_method(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new CommunityCombinedRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
        }

        Assert.Equal(Scenarios[name].Calls, run.Count);
    }

    // A test with no Python original: the method with a canceled token throws OperationCanceledException (R8) and makes
    // no fake call.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new CommunityCombinedRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        Assert.Equal(0, run.Count);
    }

    // A test with no Python original: the token is canceled at fake call k. Call k is the last call that a fake records,
    // and the method throws OperationCanceledException (R8). The retry of a failed batch does not catch the cancel.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(string name, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new CommunityCombinedRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: k);
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));

            // The calls that a failed gather leaves run on. Give them turns before the count (R25).
            await LoopGuard.RunAsync(Turns);
        }

        Assert.Equal(k + 1, run.Count);
    }

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    private static OrderedDictionary<string, GraphTypeDescriptor> BuildTypes()
    {
        var types = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var (name, doc) in Dict(Values["types"]))
        {
            types[name] = new GraphTypeDescriptor(name, (string?)doc, []);
        }

        return types;
    }

    private static OrderedDictionary<string, GraphTypeDescriptor>? TypesOf(object? names) =>
        names is null
            ? null
            : List(names).Select(n => (string)n!).Aggregate(
                new OrderedDictionary<string, GraphTypeDescriptor>(),
                (acc, n) =>
                {
                    acc[n] = Types[n];
                    return acc;
                });

    private static OrderedDictionary<(string, string), List<string>>? TypeMap(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var map = new OrderedDictionary<(string, string), List<string>>();
        foreach (var entry in List(value).Select(List))
        {
            var key = List(entry[0]);
            map[((string)key[0]!, (string)key[1]!)] = List(entry[1]).Select(x => (string)x!).ToList();
        }

        return map;
    }

    private static async Task<object?> CallAsync(CommunityCombinedRun run, CancellationToken cancellationToken)
    {
        var input = run.Input;
        EpisodeOrEpisodes episode = input["episode"] is List<object?> many
            ? new EpisodeOrEpisodes.Many { Episodes = [.. many.Select(Episode)] }
            : new EpisodeOrEpisodes.One { Episode = Episode(input["episode"]) };
        var (nodes, edges, map) = await CombinedExtractionModule.ExtractNodesAndEdgesAsync(
            run.Clients,
            episode,
            [.. List(input["previous"]).Select(Episode)],
            entityTypes: TypesOf(input["entity_types"]),
            excludedEntityTypes: input["excluded"] is List<object?> excluded ? [.. excluded.Select(x => (string)x!)] : null,
            edgeTypeMap: TypeMap(input["type_map"]),
            edgeTypes: TypesOf(input["edge_types"]),
            strictEdgeTypes: (bool)input["strict"]!,
            customExtractionInstructions: (string?)input["instructions"],
            timeline: input["timeline"] is List<object?> timeline ? [.. timeline.Select(Episode)] : null,
            cancellationToken: cancellationToken);
        // The map is a list of pairs, so that the placeholders reach its keys.
        var mapView = map.Select(e => (object?)new List<object?> { e.Key, e.Value.Select(x => (object?)x).ToList() }).ToList();
        return new OrderedDictionary<string, object?>
        {
            ["nodes"] = nodes.Select(n => (object?)NodeView(n)).ToList(),
            ["edges"] = edges.Select(e => (object?)EdgeView(e)).ToList(),
            ["map"] = mapView,
        };
    }
}
