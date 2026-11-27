// Evaluation test for graphiti_core/decorators.py (port/RULES.md, R23 and R25).
// values/handle_multiple_group_ids.json holds the calls that port/tools/record_handle_multiple_group_ids.py recorded from Python.
// A case records the result, one ordered list of events (each clone call, the entry of the decorated function, and its
// record of the call after a yield), and the largest count of calls that ran at the same time.
namespace Graphiti.Port.Evaluation;

using System.Collections.Generic;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

public class DecoratorsValuesTests
{
    private static readonly List<object?> Cases = Load();

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
    public void The_recording_holds_each_branch_and_each_limit()
    {
        Assert.Equal(432, Cases.Count);
        var recorded = Cases.Cast<OrderedDictionary<string, object?>>().ToList();
        Assert.Equal(new List<object?> { null, 1L, 2L }, recorded.Select(c => c["max"]).Distinct().OrderBy(m => m as long? ?? -1).ToList());
        Assert.Contains(recorded, c => c["group_ids"] is List<object?> { Count: 4 } && c["max"] is 2L && (long)c["max_in_flight"]! == 2);
        Assert.Contains(recorded, c => c["group_ids"] is List<object?> { Count: 4 } && c["max"] is null && (long)c["max_in_flight"]! == 4);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task The_wrapper_gives_each_recorded_result_events_and_limit_of_python(int index)
    {
        var recorded = (OrderedDictionary<string, object?>)Cases[index]!;
        var provider = (string)recorded["provider"]!;
        var shape = (string)recorded["shape"]!;
        var groupIds = ((List<object?>?)recorded["group_ids"])?.Select(g => (string)g!).ToList();
        var caller = (bool)recorded["caller"]!;
        var max = recorded["max"] is long m ? m : (long?)null;

        List<object?> events = [];
        var inFlight = 0;
        var maxInFlight = 0;
        var clientsDriver = provider == "none"
            ? null
            : new FakeGroupDriver("reggraph", provider == "falkordb" ? GraphProvider.Falkordb : GraphProvider.Neo4j, events);
        GraphDriver? callerDriver = caller ? new FakeGroupDriver("caller", GraphProvider.Neo4j, events) : null;

        // The fake records its entry before its first yield and its call after the yield, and counts the calls that
        // run at the same time (R25).
        void Enter(List<string>? ids, GraphDriver? d)
        {
            events.Add(new List<object?> { "enter", ids?.Cast<object?>().ToList(), d?.Database });
            maxInFlight = Math.Max(maxInFlight, ++inFlight);
        }

        void See(List<string>? ids, GraphDriver? d) =>
            events.Add(new List<object?> { "seen", ids?.Cast<object?>().ToList(), d?.Database });

        object? actual = await LoopGuard.RunAsync<object?>(async () =>
        {
            switch (shape)
            {
                case "list":
                    var list = await Decorators.HandleMultipleGroupIdsAsync(
                        clientsDriver, max, groupIds, callerDriver, async Task<List<string>> (ids, d) =>
                        {
                            Enter(ids, d);
                            try
                            {
                                await Task.Yield();
                                See(ids, d);
                                return [$"{GroupIdsHost.GroupIdsText(ids)}:{d?.Database ?? "None"}"];
                            }
                            finally
                            {
                                inFlight--;
                            }
                        },
                        TestContext.Current.CancellationToken);
                    return list.Cast<object?>().ToList();
                case "search":
                    var search = await Decorators.HandleMultipleGroupIdsAsync(
                        clientsDriver, max, groupIds, callerDriver, async Task<SearchResults> (ids, d) =>
                        {
                            Enter(ids, d);
                            try
                            {
                                await Task.Yield();
                                See(ids, d);
                                var all = ids ?? [];
                                return new SearchResults
                                {
                                    EdgeRerankerScores = [all.Sum(i => i.Sum(c => (double)c))],
                                    NodeRerankerScores = [all.Count],
                                };
                            }
                            finally
                            {
                                inFlight--;
                            }
                        },
                        TestContext.Current.CancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["edge"] = search.EdgeRerankerScores.Cast<object?>().ToList(),
                        ["node"] = search.NodeRerankerScores.Cast<object?>().ToList(),
                    };
                default:
                    var tuple = await Decorators.HandleMultipleGroupIdsAsync(
                        clientsDriver, max, groupIds, callerDriver, async Task<(List<string> First, List<string> Second)> (ids, d) =>
                        {
                            Enter(ids, d);
                            try
                            {
                                await Task.Yield();
                                See(ids, d);
                                return ([$"a:{GroupIdsHost.GroupIdsText(ids)}:{d?.Database ?? "None"}"], [d?.Database ?? "-"]);
                            }
                            finally
                            {
                                inFlight--;
                            }
                        },
                        TestContext.Current.CancellationToken);
                    return new List<object?>
                    {
                        tuple.First.Cast<object?>().ToList(),
                        tuple.Second.Cast<object?>().ToList(),
                    };
            }
        });

        var got = PyJson.Dumps(new List<object?> { actual, events, (long)maxInFlight });
        var want = PyJson.Dumps(new List<object?> { recorded["result"], recorded["events"], recorded["max_in_flight"] });
        Assert.Equal(want, got);
    }

    // No Python original: each branch of the wrapper with a canceled token throws OperationCanceledException (R8, R25).
    // The fake passes the token on and throws with ThrowIfCancellationRequested, as a driver call does. It takes the
    // token from the closure, so this theory shows that the branch awaits the call, and not that the wrapper hands
    // its own token on: the next theory shows that.
    [Theory(Timeout = 5000)]
    [InlineData("falkordb", "a,b")]
    [InlineData("falkordb", "other")]
    [InlineData("falkordb", "reggraph")]
    [InlineData("neo4j", "a,b")]
    [InlineData("none", "")]
    public async Task A_canceled_token_cancels_each_branch(string provider, string ids)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var token = source.Token;
        GraphDriver? clientsDriver = provider == "none"
            ? null
            : new FakeGroupDriver("reggraph", provider == "falkordb" ? GraphProvider.Falkordb : GraphProvider.Neo4j);
        List<string>? groupIds = ids.Length == 0 ? null : [.. ids.Split(',')];

        async Task<List<string>> Fake(List<string>? g, GraphDriver? d, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return [d?.Database ?? "None"];
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() =>
            Decorators.HandleMultipleGroupIdsAsync(clientsDriver, null, groupIds, null, (g, d) => Fake(g, d, token), token)));
    }

    // No Python original (R8, R25). The fake reads no token, so only the wrapper can stop the groups: with a canceled
    // token, the multi-group branch must pass it to SemaphoreGatherAsync, which starts no call. The fake of the
    // single-group branches and of the normal branch gets no token from the wrapper (R16: the body is a lambda of the
    // group_ids and the driver), so only this branch has the check.
    [Theory(Timeout = 5000)]
    [InlineData("a,b", null)]
    [InlineData("a,b,c,d", 2L)]
    [InlineData("a,b,c,d", 1L)]
    public async Task A_canceled_token_stops_each_group_before_a_fake_that_reads_no_token(string ids, long? max)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var calls = 0;
        var clientsDriver = new FakeGroupDriver("reggraph");

        async Task<List<string>> Fake(List<string>? g, GraphDriver? d)
        {
            calls++;
            await Task.Yield();
            return [d?.Database ?? "None"];
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() =>
            Decorators.HandleMultipleGroupIdsAsync(clientsDriver, max, [.. ids.Split(',')], null, Fake, source.Token)));
        Assert.Equal(0, calls);
    }

    private static List<object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "handle_multiple_group_ids.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        return (List<object?>)document["cases"]!;
    }
}
