// The arguments of SearchAsync and AdvancedSearchAsync on the real Neo4j, and the mapping of SearchResults (plan 0004,
// layer 9, packet P9d, fix round). Each test saves two facts through the namespaces, with values that only one fact
// has, and searches the group.
namespace Graphiti.E2E.Tests;

using Graphiti.Internal;
using Graphiti.PyCompat;
using CoreSearch = Graphiti.Core.Search;

// SearchAsync with NumResults writes the limit into a recipe object that the whole process shares (upstream defect U8).
// Another class that searches or ingests at the same time then reads that limit. So this class runs alone, and the
// test of NumResults puts the two limits back.
[Collection(SharedSearchRecipesGroup.Name)]
public sealed class GraphitiSearchArgumentsTests
{
    private const int TimeLimit = 180_000;

    private static readonly DateTimeOffset Y2020 = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Y2024 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Y2025 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> FilterKinds =>
        ["edge_types", "edge_uuids", "edge_source_node_uuids", "edge_target_node_uuids", "node_labels", "valid_at", "invalid_at", "created_at", "expired_at", "property_filters"];

    [Theory(Timeout = TimeLimit)]
    [MemberData(nameof(FilterKinds))]
    public async Task Each_search_filter_kind_keeps_only_the_matching_fact(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var seed = await SeedAsync(scope, ct);
        DateFilter[] On(ComparisonOperator op, DateTimeOffset? date = null) => [new DateFilter { ComparisonOperator = op, Date = date }];
        var filter = kind switch
        {
            "edge_types" => new SearchFilters { EdgeTypes = ["HOME_OF"] },
            "edge_uuids" => new SearchFilters { EdgeUuids = [seed.Home.Uuid] },
            "edge_source_node_uuids" => new SearchFilters { EdgeSourceNodeUuids = [seed.Lisbon.Uuid] },
            "edge_target_node_uuids" => new SearchFilters { EdgeTargetNodeUuids = [seed.Ana.Uuid] },
            "node_labels" => new SearchFilters { NodeLabels = ["Person", "Place"] },
            "valid_at" => new SearchFilters { ValidAt = [On(ComparisonOperator.GreaterThanEqual, Y2024)] },
            "invalid_at" => new SearchFilters { InvalidAt = [On(ComparisonOperator.IsNotNull)] },
            "created_at" => new SearchFilters { CreatedAt = [On(ComparisonOperator.GreaterThan, Y2025.AddMonths(3))] },
            "expired_at" => new SearchFilters { ExpiredAt = [On(ComparisonOperator.IsNotNull)] },
            "property_filters" => new SearchFilters { PropertyFilters = [new PropertyFilter { PropertyName = "since", PropertyValue = 2020L, ComparisonOperator = ComparisonOperator.Equals }] },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var all = await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group] }, ct);
        var filtered = await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group], SearchFilter = filter }, ct);

        Assert.Equal(GraphitiNamespaceMembersTests.Set(seed.Works.Uuid, seed.Home.Uuid), all.Select(e => e.Uuid).ToHashSet());
        if (kind == "property_filters")
        {
            // Upstream reads property_filters nowhere: search_filters.py declares the field, and no query uses it.
            Assert.Equal(all.Select(e => e.Uuid), filtered.Select(e => e.Uuid));
            return;
        }

        Assert.Equal([seed.Home.Uuid], filtered.Select(e => e.Uuid));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task NumResults_the_center_node_and_the_group_ids_shape_SearchAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var seed = await SeedAsync(scope, ct);

        var limits = (CoreSearch.SearchConfigRecipes.EdgeHybridSearchRrf.Limit, CoreSearch.SearchConfigRecipes.EdgeHybridSearchNodeDistance.Limit);
        try
        {
            Assert.Single(await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group], NumResults = 1 }, ct));
            Assert.Equal(2, (await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group], NumResults = 2 }, ct)).Count);
        }
        finally
        {
            (CoreSearch.SearchConfigRecipes.EdgeHybridSearchRrf.Limit, CoreSearch.SearchConfigRecipes.EdgeHybridSearchNodeDistance.Limit) = limits;
        }

        Assert.Empty(await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [Neo4jSidecar.NewGroupId()] }, ct));

        // With a center node, search reranks by the distance from the center node: the fact of the center comes first.
        var byBo = await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group], CenterNodeUuid = seed.Bo.Uuid }, ct);
        var byLisbon = await scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group], CenterNodeUuid = seed.Lisbon.Uuid }, ct);
        Assert.Equal(seed.Works.Uuid, byBo[0].Uuid);
        Assert.Equal(seed.Home.Uuid, byLisbon[0].Uuid);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_option_of_AdvancedSearchAsync_shapes_the_results()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, build: b => b.UseCrossEncoder(_ => new WordCrossEncoder()));
        var seed = await SeedAsync(scope, ct);
        var group = new AdvancedSearchOptions { GroupIds = [scope.Group] };

        // A query vector replaces the embedding of the query: no text matches "zzqq", and the vector is the one of the
        // home fact.
        Assert.Empty((await scope.Graphiti.AdvancedSearchAsync("zzqq", SearchRecipes.EdgeHybridSearchRrf, group, ct)).Edges);
        var byVector = await scope.Graphiti.AdvancedSearchAsync(
            "zzqq", SearchRecipes.EdgeHybridSearchRrf, group with { QueryVector = GraphitiNamespaceMembersTests.Vector(seed.Home.Fact) }, ct);
        Assert.Equal([seed.Home.Uuid], byVector.Edges.Select(e => e.Uuid));

        // A breadth-first search follows the edges out of the origin nodes: Ana -> Bo Corp, not Lisbon -> Ana.
        Assert.Empty((await scope.Graphiti.AdvancedSearchAsync("zzqq", SearchRecipes.EdgeHybridSearchCrossEncoder, group, ct)).Edges);
        var byOrigin = await scope.Graphiti.AdvancedSearchAsync(
            "zzqq", SearchRecipes.EdgeHybridSearchCrossEncoder, group with { BfsOriginNodeUuids = [seed.Ana.Uuid] }, ct);
        Assert.Equal([seed.Works.Uuid], byOrigin.Edges.Select(e => e.Uuid));

        // The center node of the node distance recipe.
        var byBo = await scope.Graphiti.AdvancedSearchAsync("Ana", SearchRecipes.EdgeHybridSearchNodeDistance, group with { CenterNodeUuid = seed.Bo.Uuid }, ct);
        var byLisbon = await scope.Graphiti.AdvancedSearchAsync("Ana", SearchRecipes.EdgeHybridSearchNodeDistance, group with { CenterNodeUuid = seed.Lisbon.Uuid }, ct);
        Assert.Equal(seed.Works.Uuid, byBo.Edges[0].Uuid);
        Assert.Equal(seed.Home.Uuid, byLisbon.Edges[0].Uuid);

        // A filter.
        var filtered = await scope.Graphiti.AdvancedSearchAsync(
            "Ana", SearchRecipes.EdgeHybridSearchRrf, group with { SearchFilter = new SearchFilters { EdgeTypes = ["WORKS_AT"] } }, ct);
        Assert.Equal([seed.Works.Uuid], filtered.Edges.Select(e => e.Uuid));

        // The reranker minimum score of the config. WordCrossEncoder gives the home fact 1.0 and the work fact 0.5.
        var both = await scope.Graphiti.AdvancedSearchAsync("Ana home", SearchRecipes.EdgeHybridSearchCrossEncoder, group, ct);
        Assert.Equal([seed.Home.Uuid, seed.Works.Uuid], both.Edges.Select(e => e.Uuid));
        Assert.Equal([1.0, 0.5], both.EdgeRerankerScores);
        var high = await scope.Graphiti.AdvancedSearchAsync(
            "Ana home", SearchRecipes.EdgeHybridSearchCrossEncoder with { RerankerMinScore = 0.6 }, group, ct);
        Assert.Equal([seed.Home.Uuid], high.Edges.Select(e => e.Uuid));
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(1, new[] { "Ana" })]
    [InlineData(2, new[] { "Ana", "Bo Corp" })]
    public async Task The_bfs_max_depth_of_a_node_config_limits_the_breadth_first_search(int depth, string[] names)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var seed = await SeedAsync(scope, ct);
        var config = new SearchConfig
        {
            NodeConfig = new NodeSearchConfig { SearchMethods = [NodeSearchMethod.Bfs], BfsMaxDepth = depth },
        };

        // node_bfs_search follows the edges out of the origin: Lisbon -> Ana at depth 1, then Ana -> Bo Corp at depth 2.
        var results = await scope.Graphiti.AdvancedSearchAsync(
            "zzqq", config, new AdvancedSearchOptions { GroupIds = [scope.Group], BfsOriginNodeUuids = [seed.Lisbon.Uuid] }, ct);

        Assert.Equal(names, results.Nodes.Select(n => n.Name).Order(StringComparer.Ordinal));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_list_and_each_score_list_of_SearchResults_equals_the_ported_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        await SeedAsync(scope, ct);
        foreach (var (name, content) in new[] { ("x1", "Ana flew to Lisbon."), ("x2", "Ana met the team of Bo Corp. Ana smiled.") })
        {
            await scope.Graphiti.Nodes.Episode.SaveAsync(GraphitiNamespaceMembersTests.Episode(scope.Group, name, EpisodeType.Text, Y2024) with { Content = content }, ct);
        }

        foreach (var name in new[] { "Ana circle", "Friends of Ana and of Ana" })
        {
            await scope.Graphiti.Nodes.Community.SaveAsync(new CommunityNode { Name = name, GroupId = scope.Group }, ct);
        }

        var results = await scope.Graphiti.AdvancedSearchAsync(
            "Ana", SearchRecipes.CombinedHybridSearchRrf, new AdvancedSearchOptions { GroupIds = [scope.Group] }, ct);
        var ported = await Asyncio.Run(() => ((GraphitiFacade)scope.Graphiti).Ported.AdvancedSearchAsync(
            "Ana", CoreSearch.SearchConfigRecipes.CombinedHybridSearchRrf, [scope.Group], cancellationToken: ct));

        Assert.Equal(ported.Edges.Select(e => e.Uuid), results.Edges.Select(e => e.Uuid));
        Assert.Equal(ported.EdgeRerankerScores, results.EdgeRerankerScores);
        Assert.Equal(ported.Nodes.Select(n => n.Uuid), results.Nodes.Select(n => n.Uuid));
        Assert.Equal(ported.NodeRerankerScores, results.NodeRerankerScores);
        Assert.Equal(ported.Episodes.Select(n => n.Uuid), results.Episodes.Select(n => n.Uuid));
        Assert.Equal(ported.EpisodeRerankerScores, results.EpisodeRerankerScores);
        Assert.Equal(ported.Communities.Select(n => n.Uuid), results.Communities.Select(n => n.Uuid));
        Assert.Equal(ported.CommunityRerankerScores, results.CommunityRerankerScores);

        // Each list has two or more items with distinct scores, so an order or a list that changes is seen.
        foreach (var scores in new[] { results.EdgeRerankerScores, results.NodeRerankerScores, results.EpisodeRerankerScores, results.CommunityRerankerScores })
        {
            Assert.True(scores.Count >= 2);
            Assert.Equal(scores.OrderDescending(), scores);
            Assert.Equal(scores.Count, scores.Distinct().Count());
        }
    }

    internal static async Task<Seed> SeedAsync(GraphitiScope scope, CancellationToken ct)
    {
        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
        var nodes = scope.Graphiti.Nodes.Entity;
        var ana = await nodes.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group, Labels = ["Person"], Summary = "Ana is a pilot." }, ct);
        var bo = await nodes.SaveAsync(new EntityNode { Name = "Bo Corp", GroupId = scope.Group, Labels = ["Org"], Summary = "Employer of Ana." }, ct);
        var lisbon = await nodes.SaveAsync(new EntityNode { Name = "Lisbon", GroupId = scope.Group, Labels = ["Place"], Summary = "Ana lives here. Home of Ana." }, ct);
        var works = await scope.Graphiti.Edges.Entity.SaveAsync(
            new EntityEdge
            {
                GroupId = scope.Group,
                SourceNodeUuid = ana.Uuid,
                TargetNodeUuid = bo.Uuid,
                CreatedAt = Y2025,
                Name = "WORKS_AT",
                Fact = "Ana works at Bo Corp.",
                ValidAt = Y2020,
            },
            ct);
        var home = await scope.Graphiti.Edges.Entity.SaveAsync(
            new EntityEdge
            {
                GroupId = scope.Group,
                SourceNodeUuid = lisbon.Uuid,
                TargetNodeUuid = ana.Uuid,
                CreatedAt = Y2025.AddMonths(5),
                Name = "HOME_OF",
                Fact = "Lisbon is the home of Ana.",
                ValidAt = Y2024,
                InvalidAt = Y2025,
                ExpiredAt = Y2025.AddMonths(1),
                Attributes = new Dictionary<string, object?> { ["since"] = 2020L },
            },
            ct);
        return new Seed(ana, bo, lisbon, works, home);
    }

    internal sealed record Seed(EntityNode Ana, EntityNode Bo, EntityNode Lisbon, EntityEdge Works, EntityEdge Home);
}

/// <summary>
/// The tests that write a limit into the recipe objects that <c>Graphiti.search</c> shares in the process (upstream
/// defect U8). A search or an ingestion of another class reads those objects, so this collection runs alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedSearchRecipesGroup
{
    public const string Name = "Shared search recipes";
}
