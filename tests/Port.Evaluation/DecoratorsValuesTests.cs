// Evaluation test for graphiti_core/decorators.py (port/RULES.md, R23 and R25).
// values/handle_multiple_group_ids.json holds the calls that port/tools/record_handle_multiple_group_ids.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.Collections.Generic;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

public class DecoratorsValuesTests
{
    [Fact]
    public async Task The_wrapper_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "handle_multiple_group_ids.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.Equal(252, cases.Count);

        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var provider = (string)recorded["provider"]!;
            var shape = (string)recorded["shape"]!;
            var groupIds = ((List<object?>?)recorded["group_ids"])?.Select(g => (string)g!).ToList();
            var caller = (bool)recorded["caller"]!;
            var max = recorded["max"] is long m ? m : (long?)null;
            var label = $"{provider} {shape} {PyJson.Dumps(recorded["group_ids"])} caller={caller} max={max}";

            var clientsDriver = provider == "none"
                ? null
                : new FakeGroupDriver("reggraph", provider == "falkordb" ? GraphProvider.Falkordb : GraphProvider.Neo4j);
            GraphDriver? callerDriver = caller ? new FakeGroupDriver("caller", GraphProvider.Neo4j) : null;
            List<object?> seen = [];

            void See(List<string>? ids, GraphDriver? d) =>
                seen.Add(new List<object?> { ids?.Cast<object?>().ToList(), d?.Database });

            try
            {
                object? actual = await Asyncio.Run<object?>(async () =>
                {
                    switch (shape)
                    {
                        case "list":
                            var list = await Decorators.HandleMultipleGroupIdsAsync(
                                clientsDriver, max, groupIds, callerDriver, async Task<List<string>> (ids, d) =>
                                {
                                    await Task.Yield();
                                    See(ids, d);
                                    return [$"{GroupIdsHost.GroupIdsText(ids)}:{d?.Database ?? "None"}"];
                                });
                            return list.Cast<object?>().ToList();
                        case "search":
                            var search = await Decorators.HandleMultipleGroupIdsAsync(
                                clientsDriver, max, groupIds, callerDriver, async Task<SearchResults> (ids, d) =>
                                {
                                    await Task.Yield();
                                    See(ids, d);
                                    var all = ids ?? [];
                                    return new SearchResults
                                    {
                                        EdgeRerankerScores = [all.Sum(i => i.Sum(c => (double)c))],
                                        NodeRerankerScores = [all.Count],
                                    };
                                });
                            return new OrderedDictionary<string, object?>
                            {
                                ["edge"] = search.EdgeRerankerScores.Cast<object?>().ToList(),
                                ["node"] = search.NodeRerankerScores.Cast<object?>().ToList(),
                            };
                        default:
                            var tuple = await Decorators.HandleMultipleGroupIdsAsync(
                                clientsDriver, max, groupIds, callerDriver, async Task<(List<string> First, List<string> Second)> (ids, d) =>
                                {
                                    await Task.Yield();
                                    See(ids, d);
                                    return ([$"a:{GroupIdsHost.GroupIdsText(ids)}:{d?.Database ?? "None"}"], [d?.Database ?? "-"]);
                                });
                            return new List<object?>
                            {
                                tuple.First.Cast<object?>().ToList(),
                                tuple.Second.Cast<object?>().ToList(),
                            };
                    }
                });

                var cloneCalls = (clientsDriver?.CloneCalls ?? []).Cast<object?>().ToList();
                var got = PyJson.Dumps(new List<object?> { actual, seen, cloneCalls });
                var want = PyJson.Dumps(new List<object?> { recorded["result"], recorded["seen"], recorded["clone_calls"] });
                if (got != want)
                {
                    failures.Add($"{label}: expected {want} but got {got}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} calls differ from Python:\n" + string.Join("\n", failures.Take(10)));
    }

    // No Python original: each branch of the wrapper with a canceled token throws OperationCanceledException (R8, R25).
    // The fake passes the token on and throws with ThrowIfCancellationRequested, as a driver call does.
    [Theory]
    [InlineData("falkordb", "a,b")]
    [InlineData("falkordb", "other")]
    [InlineData("falkordb", "reggraph")]
    [InlineData("neo4j", "a,b")]
    [InlineData("none", "")]
    public async Task A_canceled_token_cancels_each_branch(string provider, string ids)
    {
        using var source = new CancellationTokenSource();
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() =>
            Decorators.HandleMultipleGroupIdsAsync(clientsDriver, null, groupIds, null, (g, d) => Fake(g, d, token), token)));
    }
}
