// Ported from graphiti_core/ingestion/graph_reader.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Ingestion;

using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

// PORT: the module name equals the class name GraphReader, and the module has no module-level function or constant,
// so it has no static class (R2).

/// <summary>
/// The graph reads of the commit and of the enrichment.
///
/// The commit reads the graph only through this class, so a test can replace the
/// class with an in-memory object. Each search uses the stored embedding of the
/// record and does not call the embedder.
/// </summary>
internal class GraphReader
{
    internal GraphReader(GraphitiClients clients)
    {
        Clients = clients;
    }

    internal GraphitiClients Clients { get; set; }

    /// <summary>Return the dedupe candidates of each node, with the searches of serial ingestion.</summary>
    internal virtual async Task<List<List<EntityNode>>> NodeCandidatesAsync(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        return await NodeOperations.CollectCandidateNodesAsync(
            Clients,
            nodes,
            identityProperties: identityProperties,
            reuseNameEmbeddings: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>Return the stored edges between the endpoints of each edge, in either direction.</summary>
    internal virtual async Task<List<List<EntityEdge>>> EdgeDuplicateCandidatesAsync(
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        var results = await Helpers.SemaphoreGatherAsync(
            [
                .. edges.Select(edge => (Func<Task<SearchResults>>)(() => SearchModule.SearchAsync(
                    Clients,
                    edge.Fact,
                    groupIds: [edge.GroupId],
                    config: SearchConfigRecipes.EdgeHybridSearchRrf,
                    searchFilter: EdgeOperations.PairSearchFilter(edge),
                    queryVector: edge.FactEmbedding,
                    cancellationToken: cancellationToken))),
            ],
            Clients.MaxCoroutines,
            cancellationToken);
        return [.. Py.ZipStrict(edges, results).Select(pair => EdgeOperations.SamePairEdges(pair.First, pair.Second.Edges))];
    }

    /// <summary>Return the graph-wide contradiction candidates of each edge, with no endpoint filter.</summary>
    internal virtual async Task<List<List<EntityEdge>>> EdgeInvalidationCandidatesAsync(
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        var results = await Helpers.SemaphoreGatherAsync(
            [
                .. edges.Select(edge => (Func<Task<SearchResults>>)(() => SearchModule.SearchAsync(
                    Clients,
                    edge.Fact,
                    groupIds: [edge.GroupId],
                    config: SearchConfigRecipes.EdgeHybridSearchRrf,
                    searchFilter: new SearchFilters().Validate(),
                    queryVector: edge.FactEmbedding,
                    cancellationToken: cancellationToken))),
            ],
            Clients.MaxCoroutines,
            cancellationToken);
        return [.. results.Select(result => new List<EntityEdge>(result.Edges))];
    }

    /// <summary>Return the current state of the nodes that exist.</summary>
    internal virtual async Task<List<EntityNode>> NodesByUuidsAsync(
        List<string> uuids,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        try
        {
            return await EntityNode.GetByUuidsAsync(Clients.Driver, uuids, groupId: groupId, cancellationToken: cancellationToken);
        }
        catch (NodeNotFoundException)
        {
            return [];
        }
    }

    /// <summary>Return the current state of the episode nodes that exist.</summary>
    internal virtual async Task<List<EpisodicNode>> EpisodesByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        return await EpisodicNode.GetByUuidsAsync(Clients.Driver, uuids, cancellationToken: cancellationToken);
    }

    /// <summary>Return the current state of the edges that exist.</summary>
    internal virtual async Task<List<EntityEdge>> EdgesByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        try
        {
            return await EntityEdge.GetByUuidsAsync(Clients.Driver, uuids, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is EdgeNotFoundException or EdgesNotFoundException)
        {
            return [];
        }
    }
}
