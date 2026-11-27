// Ported from graphiti_core/utils/maintenance/community_operations.py

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class CommunityOperations
{
    internal const long MaxCommunityBuildConcurrency = 10;

    private static ILogger Logger => PortServices.Logger(typeof(CommunityOperations));
}

// PORT: a pydantic model (R7). It is a second class with the name of Neighbor of driver/operations/graph_utils.py, in
// its own namespace. get_community_clusters builds it from record values, so it needs ModelValidate (R7).
internal sealed record Neighbor : IPydanticModel
{
    internal required string NodeUuid { get; set; }

    internal required long EdgeCount { get; set; }

    internal Neighbor Validate() => this;

    internal static Neighbor ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Neighbor", data);
        var nodeUuid = f.Required("node_uuid", Pydantic.Str);
        var edgeCount = f.Required("edge_count", Pydantic.Int);
        f.ThrowIfAny();
        return new Neighbor { NodeUuid = nodeUuid.Value, EdgeCount = edgeCount.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("node_uuid", NodeUuid), ("edge_count", EdgeCount));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("Neighbor")
        .Required("node_uuid", PydanticType.Str)
        .Required("edge_count", PydanticType.Int);
}

internal static partial class CommunityOperations
{
    // PORT: `group_ids` is bound again to a value from a record when it is None (R6), so the unit reads that value
    // with Py.Iter.
    internal static async Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        GraphDriver driver,
        List<string>? groupIds,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.GetCommunityClustersAsync(driver, groupIds, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        List<List<EntityNode>> communityClusters = [];

        object? groupIdsValue = groupIds;
        if (groupIds is null)
        {
            var (groupIdValues, _, _) = await driver.ExecuteQueryAsync(
                "\n" + """
            MATCH (n:Entity)
            WHERE n.group_id IS NOT NULL
            RETURN
                collect(DISTINCT n.group_id) AS group_ids
""" + "\n            ",
                new OrderedDictionary<string, object?>(),
                cancellationToken);

            groupIdsValue = groupIdValues.Count > 0 ? groupIdValues[0]["group_ids"] : new List<object?>();
        }

        foreach (var groupId in Py.Iter(groupIdsValue))
        {
            var projection = new OrderedDictionary<string, List<Neighbor>>();

            // PORT: `[group_id]` is the argument `group_ids: list[str]` of a typed method, so the item of open data is
            // read as a str (R6). The query parameter below passes it as it is.
            var nodes = await EntityNode.GetByGroupIdsAsync(driver, [Py.AsString(groupId)], cancellationToken: cancellationToken);
            foreach (var node in nodes)
            {
                const string matchQuery = "\n" + """
                MATCH (n:Entity {group_id: $group_id, uuid: $uuid})-[e:RELATES_TO]-(m: Entity {group_id: $group_id})
""" + "\n            ";
                var (records, _, _) = await driver.ExecuteQueryAsync(
                    matchQuery
                    + "\n" + """
                WITH count(e) AS count, m.uuid AS uuid
                RETURN
                    uuid,
                    count
""" + "\n                ",
                    new OrderedDictionary<string, object?>
                    {
                        ["uuid"] = node.Uuid,
                        ["group_id"] = groupId,
                    },
                    cancellationToken);

                // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
                projection[node.Uuid] =
                [
                    .. records.Select(record => Neighbor.ModelValidate(new OrderedDictionary<string, object?>
                    {
                        ["node_uuid"] = record["uuid"],
                        ["edge_count"] = record["count"],
                    })),
                ];
            }

            var clusterUuids = LabelPropagation(projection);

            communityClusters.AddRange(
                await Helpers.SemaphoreGatherAsync(
                    [.. clusterUuids.Select(cluster => (Func<Task<List<EntityNode>>>)(() => EntityNode.GetByUuidsAsync(driver, cluster, cancellationToken: cancellationToken)))],
                    maxCoroutines,
                    cancellationToken));
        }

        return communityClusters;
    }

    // PORT: upstream defect U11 (R21, GAPS F6). label_propagation does not end for some inputs. Port it as it is.
    internal static List<List<string>> LabelPropagation(OrderedDictionary<string, List<Neighbor>> projection)
    {
        // Implement the label propagation community detection algorithm.
        // 1. Start with each node being assigned its own community
        // 2. Each node will take on the community of the plurality of its neighbors
        // 3. Ties are broken by going to the largest community
        // 4. Continue until no communities change during propagation

        // PORT: enumerate(projection.keys()) is a loop with a counter. Python ints are long (R6).
        var communityMap = new OrderedDictionary<string, long>();
        long index = 0;
        foreach (var uuid in projection.Keys)
        {
            communityMap[uuid] = index;
            index++;
        }

        while (true)
        {
            var noChange = true;
            var newCommunityMap = new OrderedDictionary<string, long>();

            foreach (var (uuid, neighbors) in projection)
            {
                var currCommunity = communityMap[uuid];

                // PORT: defaultdict(int) with d[k] += n (R14).
                var communityCandidates = new OrderedDictionary<long, long>();
                foreach (var neighbor in neighbors)
                {
                    var key = communityMap[neighbor.NodeUuid];
                    communityCandidates[key] = Py.Get(communityCandidates, key, 0L) + neighbor.EdgeCount;
                }

                var communityLst = communityCandidates
                    .Select(item => (Count: item.Value, Community: item.Key))
                    .ToList();

                // PORT: sort(reverse=True) on (count, community) tuples of ints is OrderByDescending and
                // ThenByDescending (R14).
                communityLst = [.. communityLst.OrderByDescending(item => item.Count).ThenByDescending(item => item.Community)];
                var (candidateRank, communityCandidate) = communityLst.Count > 0 ? communityLst[0] : (0L, -1L);
                long newCommunity;
                if (communityCandidate != -1 && candidateRank > 1)
                {
                    newCommunity = communityCandidate;
                }
                else
                {
                    newCommunity = Math.Max(communityCandidate, currCommunity);
                }

                newCommunityMap[uuid] = newCommunity;

                if (newCommunity != currCommunity)
                {
                    noChange = false;
                }
            }

            if (noChange)
            {
                break;
            }

            communityMap = newCommunityMap;
        }

        var communityClusterMap = new OrderedDictionary<long, List<string>>();
        foreach (var (uuid, community) in communityMap)
        {
            Py.SetDefault(communityClusterMap, community, new List<string>()).Add(uuid);
        }

        List<List<string>> clusters = [.. communityClusterMap.Values];
        return clusters;
    }

    /// <summary>
    /// Summarize a pair of node summaries.
    ///
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    // PORT: `clients` is keyword-only in Python. `llm_response.get('summary', '')` is open data, and
    // truncate_at_sentence takes a str, so the value is read as a str (R6).
    internal static async Task<string> SummarizePairAsync(
        LLMClient llmClient,
        (string, string) summaryPair,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        // Prepare context for LLM
        var context = new OrderedDictionary<string, object?>
        {
            ["node_summaries"] = new[] { summaryPair.Item1, summaryPair.Item2 }
                .Select(summary => (object?)new OrderedDictionary<string, object?> { ["summary"] = summary })
                .ToList(),
        };

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "summarize_nodes.summarize_pair",
            Lib.PromptLibrary.SummarizeNodes.SummarizePair,
            context,
            clients: clients,
            responseModel: Summary.ResponseModel,
            cancellationToken: cancellationToken);

        var pairSummary = Py.AsString(Py.Get(llmResponse, "summary", (object?)string.Empty));

        return TextUtils.TruncateAtSentence(pairSummary, TextUtils.MaxSummaryChars);
    }

    /// <summary>
    /// Generate a description for a community summary.
    ///
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    // PORT: `llm_response.get('description', '')` is open data, and the function returns a str, so the value is read
    // as a str (R6).
    internal static async Task<string> GenerateSummaryDescriptionAsync(
        LLMClient llmClient,
        string summary,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        var context = new OrderedDictionary<string, object?>
        {
            ["summary"] = summary,
        };

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "summarize_nodes.summary_description",
            Lib.PromptLibrary.SummarizeNodes.SummaryDescription,
            context,
            clients: clients,
            responseModel: SummaryDescription.ResponseModel,
            cancellationToken: cancellationToken);

        var description = Py.AsString(Py.Get(llmResponse, "description", (object?)string.Empty));

        return description;
    }

    /// <summary>
    /// Build one community from its nodes.
    ///
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    // PORT: the arguments after `community_cluster` are keyword-only in Python. `int(length / 2)` of an even length
    // is `length / 2`. `str(x)` of a str is the str.
    internal static async Task<(CommunityNode, List<CommunityEdge>)> BuildCommunityAsync(
        LLMClient llmClient,
        List<EntityNode> communityCluster,
        GraphitiClients? clients = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        List<string> summaries = [.. communityCluster.Select(entity => entity.Summary)];
        var length = summaries.Count;
        while (length > 1)
        {
            string? oddOneOut = null;
            if (length % 2 == 1)
            {
                oddOneOut = summaries[^1];
                summaries.RemoveAt(summaries.Count - 1);
                length -= 1;
            }

            var half = length / 2;
            List<string> newSummaries =
            [
                .. await Helpers.SemaphoreGatherAsync(
                    [
                        .. summaries[..half].Zip(summaries[half..]).Select(pair => (Func<Task<string>>)(() => SummarizePairAsync(
                            llmClient,
                            (pair.First, pair.Second),
                            clients: clients,
                            cancellationToken: cancellationToken))),
                    ],
                    maxCoroutines,
                    cancellationToken),
            ];
            if (oddOneOut is not null)
            {
                newSummaries.Add(oddOneOut);
            }

            summaries = newSummaries;
            length = summaries.Count;
        }

        var summary = TextUtils.TruncateAtSentence(summaries[0], TextUtils.MaxSummaryChars);
        var name = await GenerateSummaryDescriptionAsync(llmClient, summary, clients: clients, cancellationToken: cancellationToken);
        var now = DatetimeUtils.UtcNow();
        var communityNode = new CommunityNode
        {
            Name = name,
            GroupId = communityCluster[0].GroupId,
            Labels = ["Community"],
            CreatedAt = now,
            Summary = summary,
        }.Validate();
        var communityEdges = EdgeOperations.BuildCommunityEdges(communityCluster, communityNode, now);

        Log.BuiltCommunity(Logger, communityNode.Uuid, communityEdges.Count);

        return (communityNode, communityEdges);
    }

    /// <summary>
    /// Build communities for the requested groups.
    ///
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    // PORT: upstream defect U16 (R21): the summaries pair in row order. `max_coroutines or MAX_...` is the limit when
    // it is not null and not 0.
    internal static async Task<(List<CommunityNode>, List<CommunityEdge>)> BuildCommunitiesAsync(
        GraphDriver driver,
        LLMClient llmClient,
        List<string>? groupIds,
        GraphitiClients? clients = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        var communityClusters = await GetCommunityClustersAsync(
            driver, groupIds, maxCoroutines: maxCoroutines, cancellationToken: cancellationToken);

        // Honour the per-instance `max_coroutines` cap when set; otherwise fall back to the
        // historical built-in cap of `MAX_COMMUNITY_BUILD_CONCURRENCY` to preserve behavior.
        // We let `semaphore_gather` enforce the cap directly rather than wrapping each
        // coroutine in a redundant `asyncio.Semaphore` — both mechanisms would bound the
        // same fan-out at the same value, and the inner cap obscures the precedence.
        List<(CommunityNode, List<CommunityEdge>)> communities =
        [
            .. await Helpers.SemaphoreGatherAsync(
                [
                    .. communityClusters.Select(cluster => (Func<Task<(CommunityNode, List<CommunityEdge>)>>)(() => BuildCommunityAsync(
                        llmClient,
                        cluster,
                        clients: clients,
                        maxCoroutines: maxCoroutines,
                        cancellationToken: cancellationToken))),
                ],
                maxCoroutines is null or 0 ? MaxCommunityBuildConcurrency : maxCoroutines,
                cancellationToken),
        ];

        List<CommunityNode> communityNodes = [];
        List<CommunityEdge> communityEdges = [];
        foreach (var community in communities)
        {
            communityNodes.Add(community.Item1);
            communityEdges.AddRange(community.Item2);
        }

        return (communityNodes, communityEdges);
    }

    // PORT: no return annotation. Each branch returns the result of an operations call with no result, or None
    // (TYPE_HINTS.md, R20), so the method returns Task.
    internal static async Task RemoveCommunitiesAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.RemoveCommunitiesAsync(driver, groupIds: groupIds, cancellationToken: cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        if (driver.GraphOps is not null)
        {
            await driver.GraphOps.RemoveCommunitiesAsync(driver, groupIds: groupIds, cancellationToken: cancellationToken);
            return;
        }

        if (groupIds is { Count: > 0 })
        {
            await driver.ExecuteQueryAsync(
                "\n" + """
            MATCH (c:Community)
            WHERE c.group_id IN $group_ids
            DETACH DELETE c
""" + "\n            ",
                new OrderedDictionary<string, object?> { ["group_ids"] = groupIds },
                cancellationToken);
        }
        else
        {
            await driver.ExecuteQueryAsync(
                "\n" + """
            MATCH (c:Community)
            DETACH DELETE c
""" + "\n            ",
                new OrderedDictionary<string, object?>(),
                cancellationToken);
        }
    }

    internal static async Task<(CommunityNode?, bool)> DetermineEntityCommunityAsync(
        GraphDriver driver,
        EntityNode entity,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.DetermineEntityCommunityAsync(
                    driver, entity, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // Check if the node is already part of a community
        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
        MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity {uuid: $entity_uuid})
        RETURN
""" + "\n        "
            + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["entity_uuid"] = entity.Uuid },
            cancellationToken);

        if (records.Count > 0)
        {
            return (Nodes.GetCommunityNodeFromRecord(records[0]), false);
        }

        // If the node has no community, add it to the mode community of surrounding entities
        const string matchQuery = "\n" + """
        MATCH (c:Community)-[:HAS_MEMBER]->(m:Entity)-[:RELATES_TO]-(n:Entity {uuid: $entity_uuid})
""" + "\n    ";
        (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery
            + "\n" + """
        RETURN
""" + "\n        "
            + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["entity_uuid"] = entity.Uuid },
            cancellationToken);

        List<CommunityNode> communities = [.. records.Select(Nodes.GetCommunityNodeFromRecord)];

        // PORT: defaultdict(int) with d[k] += n (R14).
        var communityMap = new OrderedDictionary<string, long>();
        foreach (var community in communities)
        {
            communityMap[community.Uuid] = Py.Get(communityMap, community.Uuid, 0L) + 1;
        }

        string? communityUuid = null;
        long maxCount = 0;
        foreach (var (uuid, count) in communityMap)
        {
            if (count > maxCount)
            {
                communityUuid = uuid;
                maxCount = count;
            }
        }

        if (maxCount == 0)
        {
            return (null, false);
        }

        foreach (var community in communities)
        {
            if (community.Uuid == communityUuid)
            {
                return (community, true);
            }
        }

        return (null, false);
    }

    /// <summary>
    /// Update the community that contains an entity.
    ///
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    // PORT: upstream defect U17 (R21): concurrent calls run with no lock. `clients` is keyword-only in Python.
    internal static async Task<(List<CommunityNode>, List<CommunityEdge>)> UpdateCommunityAsync(
        GraphDriver driver,
        LLMClient llmClient,
        EmbedderClient embedder,
        EntityNode entity,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        var (community, isNew) = await DetermineEntityCommunityAsync(driver, entity, cancellationToken);

        if (community is null)
        {
            return ([], []);
        }

        var newSummary = await SummarizePairAsync(
            llmClient,
            (entity.Summary, community.Summary),
            clients: clients,
            cancellationToken: cancellationToken);
        var newName = await GenerateSummaryDescriptionAsync(llmClient, newSummary, clients: clients, cancellationToken: cancellationToken);

        community.Summary = newSummary;
        community.Name = newName;

        List<CommunityEdge> communityEdges = [];
        if (isNew)
        {
            var communityEdge = EdgeOperations.BuildCommunityEdges([entity], community, DatetimeUtils.UtcNow())[0];
            await communityEdge.SaveAsync(driver, cancellationToken);
            communityEdges.Add(communityEdge);
        }

        await community.GenerateNameEmbeddingAsync(embedder, cancellationToken);

        await community.SaveAsync(driver, cancellationToken);

        return ([community], communityEdges);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Built community {Uuid} with {Count} edges")]
        internal static partial void BuiltCommunity(ILogger logger, string uuid, long count);
    }
}
