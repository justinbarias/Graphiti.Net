// Ported from tests/utils/maintenance/test_edge_dedup_scenarios.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/maintenance/edge_operations.py, `resolve_extracted_edges`.
//
// PORT: the Python harness patches `edge_ops.search` (open item O10). The real search reads the search interface of the
// driver for its two edge searches, so the fake answers there with the same ranked pool (EdgeTestSearchDriver). RRF of
// two equal lists keeps their order. `searches` counts the full-text call of each search, one for each search. The
// `_report` entry point of the Python file is a command-line tool, not a test, and has no port.
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

public partial class TestEdgeDedupScenarios
{
    private static readonly PyDateTime Now = PyDateTime.FromIsoFormat("2026-06-10T00:00:00+00:00");

    private static readonly Dictionary<string, Scenario> Scenarios = new()
    {
        // The hazard: an identical fact between a *different* node pair must
        // not absorb the new fact (the prompt-level judge cannot see
        // endpoints, so offering it as a duplicate candidate merges it).
        ["cross_endpoint_identical_text"] = new(
            [MakeEdge("E1", "dan_jones", "yoga", "Dan likes yoga")],
            ("dan_smith", "yoga", "Dan likes yoga"),
            ["dan_jones", "dan_smith", "yoga"]),

        // Parity: an exact same-pair duplicate resolves via the fast path
        // with no LLM call.
        ["same_pair_duplicate"] = new(
            [MakeEdge("E2", "user", "yoga", "User likes yoga")],
            ("user", "yoga", "  user LIKES yoga "),
            ["user", "yoga"]),

        // The undirected candidate fetch must catch a duplicate stored in
        // the opposite orientation.
        ["reverse_direction_duplicate"] = new(
            [MakeEdge("E3", "bob", "alice", "Alice manages Bob")],
            ("alice", "bob", "Alice manages Bob"),
            ["alice", "bob"]),

        // A contradicting fact on a new node pair must reach the LLM as a
        // properly labeled invalidation candidate (historically this list
        // was emptied by the overlap dedup whenever the duplicate search
        // degenerated to graph-wide).
        ["contradiction_on_new_pair"] = new(
            [MakeEdge("E4", "alice", "acme", "Alice works at Acme")],
            ("alice", "betacorp", "Alice works at BetaCorp"),
            ["alice", "acme", "betacorp"]),
    };

    [Fact(Timeout = 5000)]
    public async Task test_cross_endpoint_identical_text_is_not_merged()
    {
        var report = await RunScenario(Scenarios["cross_endpoint_identical_text"], TestContext.Current.CancellationToken);
        Assert.Equal(["pair-filtered", "graph-wide"], report.Searches);
        Assert.Equal([], report.ExistingFacts!); // no duplicate candidates from other node pairs
        Assert.False(report.MergedCrossEndpoint);
        Assert.Equal(report.ExtractedUuid, report.ResolvedUuid);

        // The similar fact still reaches the LLM, properly labeled.
        Assert.Equal(["Dan likes yoga"], report.InvalidationCandidates!);
    }

    [Fact(Timeout = 5000)]
    public async Task test_same_pair_duplicate_fast_path_one_search()
    {
        var report = await RunScenario(Scenarios["same_pair_duplicate"], TestContext.Current.CancellationToken);
        Assert.Equal("E2", report.ResolvedUuid);
        Assert.False(report.LlmCalled, "exact match resolves without an LLM call");
        Assert.Equal(["pair-filtered", "graph-wide"], report.Searches);
    }

    [Fact(Timeout = 5000)]
    public async Task test_reverse_direction_duplicate_is_caught()
    {
        var report = await RunScenario(Scenarios["reverse_direction_duplicate"], TestContext.Current.CancellationToken);
        Assert.Equal("E3", report.ResolvedUuid);
        Assert.Equal(["pair-filtered", "graph-wide"], report.Searches);
    }

    [Fact(Timeout = 5000)]
    public async Task test_contradiction_on_new_pair_reaches_invalidation_list()
    {
        var report = await RunScenario(Scenarios["contradiction_on_new_pair"], TestContext.Current.CancellationToken);

        // invalidation candidates must not be cannibalized by duplicate selection
        Assert.Equal(["Alice works at Acme"], report.InvalidationCandidates!);
        Assert.Equal([], report.ExistingFacts!);
        Assert.Equal(1, report.Invalidated);
        Assert.False(report.MergedCrossEndpoint);
    }

    // Deterministic bag-of-words unit vector, a stand-in embedder.
    private static List<double> TextVec(string text)
    {
        var v = new double[64];
        foreach (Match tok in WordPattern().Matches(PyText.Lower(text)))
        {
            // PORT: int(md5(tok).hexdigest(), 16) % 64 is the low 6 bits of the last byte of the digest. The analyzer
            // CA5351 refuses MD5.HashData, so the digest comes from IncrementalHash, as in LlmClient/Client.cs.
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            md5.AppendData(Encoding.UTF8.GetBytes(tok.Value));
            v[md5.GetHashAndReset()[15] % 64] += 1.0;
        }

        var norm = Math.Sqrt(v.Sum(x => x * x));
        if (norm == 0)
        {
            norm = 1.0;
        }

        return [.. v.Select(x => x / norm)];
    }

    private static double Dot(List<double> a, List<double> b) => Py.ZipStrict(a, b).Sum(p => p.First * p.Second);

    private static double Jaccard(string a, string b)
    {
        var ta = WordPattern().Matches(PyText.Lower(a)).Select(m => m.Value).ToHashSet();
        var tb = WordPattern().Matches(PyText.Lower(b)).Select(m => m.Value).ToHashSet();
        var union = ta.Union(tb).Count();
        return (double)ta.Intersect(tb).Count() / (union == 0 ? 1 : union);
    }

    // PORT: each scenario uses the default age of one day, so the date is NOW - 1 day.
    private static readonly PyDateTime OneDayAgo = PyDateTime.FromIsoFormat("2026-06-09T00:00:00+00:00");

    private static EntityEdge MakeEdge(string uuid, string src, string tgt, string fact) => new EntityEdge
    {
        Uuid = uuid,
        SourceNodeUuid = src,
        TargetNodeUuid = tgt,
        Name = "RELATES_TO",
        GroupId = "g1",
        Fact = fact,
        Episodes = ["old_episode"],
        CreatedAt = OneDayAgo,
        ValidAt = OneDayAgo,
        InvalidAt = null,
    }.Validate();

    private static List<OrderedDictionary<string, object?>> ParseList(string message, string tag)
    {
        var m = Regex.Match(message, "<" + tag + @">\s*(\[.*?\])\s*</" + tag + ">", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return m.Success ? [.. ((List<object?>)PyAst.LiteralEval(m.Groups[1].Value)!).Cast<OrderedDictionary<string, object?>>()] : [];
    }

    private static async Task<Report> RunScenario(Scenario scenario, CancellationToken cancellationToken)
    {
        var graphEdges = scenario.Graph;
        var (src, tgt, fact) = scenario.Extracted;
        var report = new Report();

        List<EntityEdge> Search(string? query, List<double>? vector, SearchFilters searchFilter)
        {
            var sourceIds = searchFilter.EdgeSourceNodeUuids;
            var targetIds = searchFilter.EdgeTargetNodeUuids;
            var uuids = searchFilter.EdgeUuids;
            List<EntityEdge> pool;
            string kind;
            if (sourceIds is { Count: > 0 } || targetIds is { Count: > 0 })
            {
                // Endpoint pre-filter (current design): ranking runs over the
                // endpoint-scoped subset only -- never the whole graph.
                pool = [.. graphEdges.Where(e => (sourceIds is not { Count: > 0 } || sourceIds.Contains(e.SourceNodeUuid)) && (targetIds is not { Count: > 0 } || targetIds.Contains(e.TargetNodeUuid)))];
                kind = "pair-filtered";
            }
            else if (uuids is { Count: > 0 })
            {
                pool = [.. graphEdges.Where(e => uuids.Contains(e.Uuid))];
                kind = "filtered";
            }
            else
            {
                // Old production semantics on purpose: falsy uuid list (None OR
                // empty) -> unfiltered graph-wide retrieval, so baseline runs
                // against pre-fix refs stay honest. See module docstring.
                pool = [.. graphEdges];
                kind = "graph-wide";
            }

            if (query is not null)
            {
                report.Searches.Add(kind);
            }

            var queryVec = vector ?? TextVec(query!);
            return [.. pool.OrderBy(e => -Dot(TextVec(e.Fact), queryVec)).Take(10)];
        }

        var llmClient = new EdgeTestLlm();
        llmClient.Respond = (messages, promptName) =>
        {
            if (promptName is null || !promptName.Contains("resolve_edge", StringComparison.Ordinal))
            {
                return new OrderedDictionary<string, object?>();
            }

            report.LlmCalled = true;
            var body = messages[^1].Content;
            var existing = ParseList(body, "EXISTING FACTS");
            var invalidation = ParseList(body, "FACT INVALIDATION CANDIDATES");
            var newFactMatch = Regex.Match(body, @"<NEW FACT>\s*(.*?)\s*</NEW FACT>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var newFact = newFactMatch.Success ? newFactMatch.Groups[1].Value : fact;
            report.ExistingFacts = [.. existing.Select(e => (string)e["fact"]!)];
            var byFact = new Dictionary<string, EntityEdge>();
            foreach (var e in graphEdges)
            {
                byFact[e.Fact] = e;
            }

            report.CrossEndpointShown = existing.Count(e =>
                byFact.TryGetValue((string)e["fact"]!, out var stored) && (stored.SourceNodeUuid, stored.TargetNodeUuid) != (src, tgt));
            report.InvalidationCandidates = [.. invalidation.Select(e => (string)e["fact"]!)];
            List<object?> duplicates = [.. existing.Where(e => Jaccard((string)e["fact"]!, newFact) >= 0.8).Select(e => e["idx"])];
            List<object?> contradicted = [.. existing.Concat(invalidation).Where(e => Jaccard((string)e["fact"]!, newFact) is >= 0.4 and < 0.8).Select(e => e["idx"])];
            return new OrderedDictionary<string, object?> { ["duplicate_facts"] = duplicates, ["contradicted_facts"] = contradicted };
        };

        var extracted = new EntityEdge
        {
            SourceNodeUuid = src,
            TargetNodeUuid = tgt,
            Name = "RELATES_TO",
            GroupId = "g1",
            Fact = fact,
            Episodes = [],
            CreatedAt = Now,
            ValidAt = Now, // set so _extract_edge_timestamps skips its LLM call
            InvalidAt = null,
        }.Validate();
        var episode = new EpisodicNode
        {
            Uuid = "ep1",
            Name = "ep",
            GroupId = "g1",
            Source = EpisodeType.Message,
            SourceDescription = string.Empty,
            Content = string.Empty,
            ValidAt = Now,
        }.Validate();
        List<EntityNode> nodes = [.. scenario.Nodes.Select(u => new EntityNode { Uuid = u, Name = u, GroupId = "g1", Labels = ["Entity"] }.Validate())];
        var clients = new GraphitiClients
        {
            Driver = new EdgeTestSearchDriver(Search),
            LlmClient = llmClient,
            Embedder = new EdgeTestEmbedder(TextVec),
            CrossEncoder = new EdgeOperationsRun.FakeCrossEncoder(),
            Tracer = new NoOpTracer(),
        };

        var (resolved, invalidated, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients, [extracted], episode, nodes, new(), new(), cancellationToken: cancellationToken));

        var result = resolved[0];
        report.ResolvedUuid = result.Uuid;
        report.ExtractedUuid = extracted.Uuid;
        report.MergedCrossEndpoint = (result.SourceNodeUuid, result.TargetNodeUuid) != (src, tgt);
        report.Invalidated = invalidated.Count;
        return report;
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    private sealed record Scenario(List<EntityEdge> Graph, (string Src, string Tgt, string Fact) Extracted, List<string> Nodes);

    private sealed class Report
    {
        internal List<string> Searches { get; } = [];

        internal List<string>? ExistingFacts { get; set; }

        internal int? CrossEndpointShown { get; set; }

        internal List<string>? InvalidationCandidates { get; set; }

        internal bool LlmCalled { get; set; }

        internal string ResolvedUuid { get; set; } = string.Empty;

        internal string ExtractedUuid { get; set; } = string.Empty;

        internal bool MergedCrossEndpoint { get; set; }

        internal int Invalidated { get; set; }
    }
}
