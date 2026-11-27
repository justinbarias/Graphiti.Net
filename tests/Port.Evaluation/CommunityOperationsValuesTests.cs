// Evaluation test for graphiti_core/utils/maintenance/community_operations.py (port/RULES.md, R23, R25).
// values/community_operations.json holds the calls that port/tools/record_community_operations.py recorded from Python:
// label_propagation and Neighbor with generated inputs, and the async functions with fakes for the LLM client, the
// embedder, and the graph driver. The calls and the log lines of each case are one ordered list. Each run of
// summarize_pair calls of one round is sorted on both sides, because the calls of a round run at the same time.
namespace Graphiti.Port.Evaluation;

using System.Runtime.ExceptionServices;
using Graphiti.Core;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

public class CommunityOperationsValuesTests
{
    private static readonly OrderedDictionary<string, object?> Values = CommunityCombinedValues.Load("community_operations.json");

    private static readonly PyDateTime Now = PyDateTime.FromIsoFormat((string)Values["fixed_now"]!);

    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(Values["async_cases"]).Select(Dict).ToList();

    // The scenarios of the cancellation tests: one recorded case of each async function with more than one call, and
    // its count of fake calls with no cancel.
    private static readonly Dictionary<string, (int Case, int Calls)> Scenarios = new()
    {
        ["get_community_clusters"] = (0, 14),
        ["get_community_clusters_limit_one"] = (1, 9),
        ["summarize_pair"] = (7, 1),
        ["generate_summary_description"] = (12, 1),
        ["build_community"] = (19, 8),
        ["build_community_limit_one"] = (20, 6),
        ["build_communities"] = (25, 21),
        ["remove_communities"] = (28, 1),
        ["remove_communities_graph_ops"] = (32, 1),
        ["determine_entity_community"] = (35, 2),
        ["update_community"] = (42, 7),
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
        Assert.Equal(CommunityOperations.MaxCommunityBuildConcurrency, (long)Dict(Values["constants"])["MAX_COMMUNITY_BUILD_CONCURRENCY"]!);
    }

    [Fact]
    public void The_recording_reaches_each_function()
    {
        var fns = AsyncCases.Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet();
        foreach (var fn in new[]
        {
            "get_community_clusters", "summarize_pair", "generate_summary_description", "build_community", "build_communities",
            "remove_communities", "determine_entity_community", "update_community",
        })
        {
            Assert.Contains(fn, fns);
        }

        // The rounds of build_community: a cluster of 8 summaries makes 4, 2, and 1 pair calls in three rounds.
        var eight = Dict(AsyncCases[19]["out"]);
        Assert.Equal(7, List(eight["events"]).Select(Dict).Count(e => e.GetValueOrDefault("prompt_name") is CommunityCombinedValues.PairPrompt));
        Assert.Equal(4L, (long)eight["max_in_flight"]!);

        // The case of test_build_communities_uses_configured_prompt_library: build_communities with a marker prompt of
        // summarize_pair. Each build_community gets the clients, so the pair calls send the marker, and the case with no
        // routing (async case 55) sends other messages.
        var routed = AsyncCases.Index().Single(c => Dict(c.Item["in"]).GetValueOrDefault("routing") is string).Index;
        Assert.Equal(58, routed);
        Assert.Equal("build_communities", Dict(AsyncCases[58]["in"])["fn"]);
        static List<object?> PairMessages(int index) =>
            [.. List(Dict(AsyncCases[index]["out"])["events"]).Select(Dict).Where(e => e.GetValueOrDefault("prompt_name") is CommunityCombinedValues.PairPrompt).Select(e => e["messages"])];
        Assert.NotEmpty(PairMessages(58));
        Assert.NotEqual(PyJson.Dumps(PairMessages(55)), PyJson.Dumps(PairMessages(58)));
    }

    // PORT: upstream defect U11: label_propagation does not end for some inputs. A wrong tie order makes a recorded
    // projection loop, so each case runs on a thread with a time limit (R25, RULES.md: a background thread and
    // Join). A case that does not end fails the test, and the thread is a background thread, so the test host lives.
    [Fact]
    public void The_port_gives_each_recorded_sync_result()
    {
        var cases = List(Values["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 150);
        List<string> failures = [];
        foreach (var (index, item) in cases.Index())
        {
            object? actual = null;
            Exception? unexpected = null;
            var worker = new Thread(() =>
            {
                try
                {
                    actual = SyncResult(item);
                }
                catch (Exception e)
                {
                    unexpected = e;
                }
            })
            {
                IsBackground = true,
            };
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), $"case {index} ({item["f"]}) did not end within 5 seconds (defect U11)");
            if (unexpected is not null)
            {
                ExceptionDispatchInfo.Capture(unexpected).Throw();
            }

            var expected = Dict(item["out"]);
            if (expected.GetValueOrDefault("error") is OrderedDictionary<string, object?> { } error && (string)error["type"]! == "KeyError")
            {
                expected = new OrderedDictionary<string, object?> { ["error"] = "KeyError" };
            }

            if (PyJson.Dumps(expected) != PyJson.Dumps(actual))
            {
                failures.Add($"case {index} ({item["f"]}):\n  expected {PyJson.Dumps(expected)}\n  got      {PyJson.Dumps(actual)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)) + $"\n{failures.Count} failures");
    }

    private static OrderedDictionary<string, object?> SyncResult(OrderedDictionary<string, object?> item)
    {
        var input = Dict(item["in"]);
        try
        {
            if ((string)item["f"]! == "label_propagation")
            {
                var projection = new OrderedDictionary<string, List<Neighbor>>();
                foreach (var (key, neighbors) in Dict(input["projection"]))
                {
                    projection[key] = [.. List(neighbors).Select(n => Neighbor.ModelValidate(Dict(n)))];
                }

                return new OrderedDictionary<string, object?>
                {
                    ["ok"] = CommunityOperations.LabelPropagation(projection).Select(c => (object?)c.Select(x => (object?)x).ToList()).ToList(),
                };
            }

            return new OrderedDictionary<string, object?> { ["ok"] = Neighbor.ModelValidate(Dict(input["data"])).ModelDump() };
        }
        catch (KeyNotFoundException)
        {
            return new OrderedDictionary<string, object?> { ["error"] = "KeyError" };
        }
        catch (ValidationException e)
        {
            return new OrderedDictionary<string, object?>
            {
                ["error"] = new OrderedDictionary<string, object?> { ["type"] = "ValidationError", ["text"] = e.Message.Split('\n')[0] },
            };
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
            catch (Exception e) when (e is ValidationException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                // .NET has another text for an IndexError, so the view writes the text of Python and the type is the test (R4).
                output["error"] = new OrderedDictionary<string, object?>
                {
                    ["type"] = CommunityCombinedValues.PythonErrorName(e),
                    ["text"] = e is ValidationException ? e.Message.Split('\n')[0] : e is ArgumentOutOfRangeException ? "list index out of range" : e.Message,
                };
            }
        }

        output["events"] = CommunityCombinedValues.SortPairRuns(run.Events);
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

    // The cases that the recorder ran again with a cancel are the cases of Scenarios (CANCEL_CASES in
    // port/tools/record_community_operations.py).
    [Fact]
    public void Each_scenario_has_a_recorded_cancel_run_for_each_point()
    {
        var recorded = AsyncCases.Index().Where(c => c.Item.ContainsKey("cancel")).Select(c => c.Index).Order().ToList();
        Assert.Equal(Scenarios.Values.Select(v => v.Case).Order().ToList(), recorded);
        foreach (var (_, (index, calls)) in Scenarios)
        {
            Assert.Equal(
                Enumerable.Range(-1, calls).Select(k => (long)k).ToList(),
                List(AsyncCases[index]["cancel"]).Select(r => (long)Dict(r)["k"]!).ToList());
        }
    }

    // Each async method with a canceled token throws OperationCanceledException (R8). The recorder canceled the task of
    // the call before its first await (k = -1), so the test compares the attempts, the recorded calls, and the events
    // that the method writes before it reads the token. A fake that is canceled throws after a yield, so a dropped token
    // or a caught cancel gives other counts.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var index = Scenarios[name].Case;
        var run = new CommunityCombinedRun(Dict(AsyncCases[index]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        CommunityCombinedValues.AssertCancelPoint(AsyncCases[index], index, -1, run, 0, LateCancel);
    }

    // The token is canceled while call k runs. Call k is the last call that a fake records. Python lets a CancelledError
    // through each `except Exception` (it is a BaseException). The recorder canceled the task of the call at the same
    // point, so the test compares the count of the attempts (each fake counts an attempt before it reads its token), the
    // count of the calls that a fake recorded, and the events after call k (the log lines and the later calls, with each
    // run of summarize_pair calls sorted). The calls that a failed gather leaves run on, so the test gives the scheduler
    // 50 turns before it counts (R25).
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(string name, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var index = Scenarios[name].Case;
        var run = new CommunityCombinedRun(Dict(AsyncCases[index]["in"]), source, cancelAt: k);
        using (PortServices.Use(loggerFactory: new CommunityCombinedLogCapture(run.Events), time: new MaintenanceFixedClock(Now.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        CommunityCombinedValues.AssertCancelPoint(AsyncCases[index], index, k, run, run.EventsAtCancel, LateCancel);
    }

    [Fact]
    public void Each_late_cancel_entry_names_one_point_of_the_recording()
    {
        Assert.Equal(LateCancel.Length, LateCancel.Select(e => (e.Case, e.K)).Distinct().Count());
        Assert.All(LateCancel, e => Assert.Contains(e.K, List(AsyncCases[e.Case]["cancel"]).Select(r => (int)(long)Dict(r)["k"]!)));
    }

    // The points where the port makes more attempts than Python (the late cancel of GAPS F33). At each point the call that
    // cancels is the last call of a gather (or of a build that runs next to the others). Python raises CancelledError where
    // the task of the caller resumes, so the next step makes no attempt. The port does not read the token again when the
    // call returns, so each caller that resumes makes its next call, and that call throws. Case 25 at calls 16 and 17 has
    // two and three more attempts, because that many builds of a community resume in the same turn.
    private static readonly (int Case, int K, string Kind)[] LateCancel =
    [
        (0, 9, "attempts"),
        (19, 6, "attempts"),
        (20, 4, "attempts"),
        (25, 9, "attempts"),
        (25, 15, "attempts"),
        (25, 16, "attempts"),
        (25, 17, "attempts"),
    ];

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    private static List<string>? Strings(object? value) => value is List<object?> list ? [.. list.Select(x => (string)x!)] : null;

    // A case with "defaults" leaves each optional argument out (R25).
    private static async Task<object?> CallAsync(CommunityCombinedRun run, CancellationToken cancellationToken)
    {
        var input = run.Input;
        var defaults = input.GetValueOrDefault("defaults") is true;
        var route = input.GetValueOrDefault("routing") is true or string ? run.Clients : null;
        var maxCoroutines = (long?)input.GetValueOrDefault("max_coroutines");
        switch ((string)input["fn"]!)
        {
            case "get_community_clusters":
                {
                    var clusters = defaults
                        ? await CommunityOperations.GetCommunityClustersAsync(run.Driver, Strings(input["group_ids"]), cancellationToken: cancellationToken)
                        : await CommunityOperations.GetCommunityClustersAsync(run.Driver, Strings(input["group_ids"]), maxCoroutines, cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["clusters"] = clusters.Select(c => (object?)c.Select(n => (object?)n.Uuid).ToList()).ToList(),
                    };
                }

            case "summarize_pair":
                {
                    var pair = List(input["pair"]);
                    var summary = defaults
                        ? await CommunityOperations.SummarizePairAsync(run.Llm, ((string)pair[0]!, (string)pair[1]!), cancellationToken: cancellationToken)
                        : await CommunityOperations.SummarizePairAsync(run.Llm, ((string)pair[0]!, (string)pair[1]!), clients: route, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?> { ["summary"] = summary };
                }

            case "generate_summary_description":
                {
                    var description = defaults
                        ? await CommunityOperations.GenerateSummaryDescriptionAsync(run.Llm, (string)input["summary"]!, cancellationToken: cancellationToken)
                        : await CommunityOperations.GenerateSummaryDescriptionAsync(run.Llm, (string)input["summary"]!, clients: route, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?> { ["description"] = description };
                }

            case "build_community":
                {
                    List<EntityNode> cluster = [.. List(input["cluster"]).Select(n => Node(n))];
                    var (community, edges) = defaults
                        ? await CommunityOperations.BuildCommunityAsync(run.Llm, cluster, cancellationToken: cancellationToken)
                        : await CommunityOperations.BuildCommunityAsync(run.Llm, cluster, clients: route, maxCoroutines: maxCoroutines, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["community"] = CommunityCombinedValues.CommunityView(community),
                        ["edges"] = edges.Select(e => (object?)CommunityCombinedValues.CommunityEdgeView(e)).ToList(),
                    };
                }

            case "build_communities":
                {
                    var (nodes, edges) = defaults
                        ? await CommunityOperations.BuildCommunitiesAsync(run.Driver, run.Llm, Strings(input["group_ids"]), cancellationToken: cancellationToken)
                        : await CommunityOperations.BuildCommunitiesAsync(run.Driver, run.Llm, Strings(input["group_ids"]), clients: route, maxCoroutines: maxCoroutines, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["communities"] = nodes.Select(n => (object?)CommunityCombinedValues.CommunityView(n)).ToList(),
                        ["edges"] = edges.Select(e => (object?)CommunityCombinedValues.CommunityEdgeView(e)).ToList(),
                    };
                }

            case "remove_communities":
                if (defaults)
                {
                    await CommunityOperations.RemoveCommunitiesAsync(run.Driver, cancellationToken: cancellationToken);
                }
                else
                {
                    await CommunityOperations.RemoveCommunitiesAsync(run.Driver, Strings(input["group_ids"]), cancellationToken);
                }

                return new OrderedDictionary<string, object?> { ["result"] = null };
            case "determine_entity_community":
                {
                    var (community, isNew) = await CommunityOperations.DetermineEntityCommunityAsync(run.Driver, Node(input["entity"]), cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["community"] = community is null ? null : CommunityCombinedValues.CommunityView(community),
                        ["is_new"] = isNew,
                    };
                }

            case "update_community":
                {
                    var (nodes, edges) = defaults
                        ? await CommunityOperations.UpdateCommunityAsync(run.Driver, run.Llm, run.Embedder, Node(input["entity"]), cancellationToken: cancellationToken)
                        : await CommunityOperations.UpdateCommunityAsync(run.Driver, run.Llm, run.Embedder, Node(input["entity"]), clients: route, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["communities"] = nodes.Select(n => (object?)CommunityCombinedValues.CommunityView(n)).ToList(),
                        ["edges"] = edges.Select(e => (object?)CommunityCombinedValues.CommunityEdgeView(e)).ToList(),
                    };
                }

            default:
                throw new InvalidOperationException((string)input["fn"]!);
        }
    }
}
