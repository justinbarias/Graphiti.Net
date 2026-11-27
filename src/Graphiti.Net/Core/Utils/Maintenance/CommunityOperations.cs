// Ported from graphiti_core/utils/maintenance/community_operations.py

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

internal static partial class CommunityOperations
{
    internal const long MaxCommunityBuildConcurrency = 10;

    private static ILogger Logger => PortServices.Logger(typeof(CommunityOperations));
}

// PORT: a pydantic model (R7). It is a second class with the name of Neighbor of driver/operations/graph_utils.py, in
// its own namespace. get_community_clusters builds it from record values, so it needs ModelValidate (R7).
internal sealed record Neighbor
{
    internal required string NodeUuid { get; set; }

    internal required long EdgeCount { get; set; }

    internal Neighbor Validate() => this;

    // TODO(port): Neighbor.model_validate
    internal static Neighbor ModelValidate(OrderedDictionary<string, object?> data)
    {
        throw new NotSupportedException("TODO(port): Neighbor.model_validate");
    }
}

internal static partial class CommunityOperations
{
    // PORT: `group_ids` is bound again to a value from a record when it is None (R6), so the unit reads that value
    // with Py.Iter.
    // TODO(port): get_community_clusters
    internal static Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        GraphDriver driver,
        List<string>? groupIds,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_community_clusters");
    }

    // PORT: upstream defect U11 (R21, GAPS F6). label_propagation does not end for some inputs. Port it as it is.
    // TODO(port): label_propagation
    internal static List<List<string>> LabelPropagation(OrderedDictionary<string, List<Neighbor>> projection)
    {
        throw new NotSupportedException("TODO(port): label_propagation");
    }

    // PORT: `clients` is keyword-only in Python.
    // TODO(port): summarize_pair
    internal static Task<string> SummarizePairAsync(
        LLMClient llmClient,
        (string, string) summaryPair,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): summarize_pair");
    }

    // TODO(port): generate_summary_description
    internal static Task<string> GenerateSummaryDescriptionAsync(
        LLMClient llmClient,
        string summary,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): generate_summary_description");
    }

    // PORT: the arguments after `community_cluster` are keyword-only in Python.
    // TODO(port): build_community
    internal static Task<(CommunityNode, List<CommunityEdge>)> BuildCommunityAsync(
        LLMClient llmClient,
        List<EntityNode> communityCluster,
        GraphitiClients? clients = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): build_community");
    }

    // PORT: upstream defect U16 (R21): the summaries pair in row order.
    // TODO(port): build_communities
    internal static Task<(List<CommunityNode>, List<CommunityEdge>)> BuildCommunitiesAsync(
        GraphDriver driver,
        LLMClient llmClient,
        List<string>? groupIds,
        GraphitiClients? clients = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): build_communities");
    }

    // PORT: no return annotation. Each branch returns the result of an operations call with no result, or None
    // (TYPE_HINTS.md, R20), so the method returns Task.
    // TODO(port): remove_communities
    internal static Task RemoveCommunitiesAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): remove_communities");
    }

    // TODO(port): determine_entity_community
    internal static Task<(CommunityNode?, bool)> DetermineEntityCommunityAsync(
        GraphDriver driver,
        EntityNode entity,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): determine_entity_community");
    }

    // PORT: upstream defect U17 (R21): concurrent calls run with no lock. `clients` is keyword-only in Python.
    // TODO(port): update_community
    internal static Task<(List<CommunityNode>, List<CommunityEdge>)> UpdateCommunityAsync(
        GraphDriver driver,
        LLMClient llmClient,
        EmbedderClient embedder,
        EntityNode entity,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): update_community");
    }
}
