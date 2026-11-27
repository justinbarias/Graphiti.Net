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

// PORT: the module name equals the class name GraphReader, and the module has no module-level function or constant,
// so it has no static class (R2).
internal class GraphReader
{
    // TODO(port): GraphReader.__init__
    internal GraphReader(GraphitiClients clients)
    {
        throw new NotSupportedException("TODO(port): GraphReader.__init__");
    }

    internal GraphitiClients Clients { get; set; }

    // TODO(port): GraphReader.node_candidates
    internal virtual Task<List<List<EntityNode>>> NodeCandidatesAsync(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.node_candidates");
    }

    // TODO(port): GraphReader.edge_duplicate_candidates
    internal virtual Task<List<List<EntityEdge>>> EdgeDuplicateCandidatesAsync(
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.edge_duplicate_candidates");
    }

    // TODO(port): GraphReader.edge_invalidation_candidates
    internal virtual Task<List<List<EntityEdge>>> EdgeInvalidationCandidatesAsync(
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.edge_invalidation_candidates");
    }

    // TODO(port): GraphReader.nodes_by_uuids
    internal virtual Task<List<EntityNode>> NodesByUuidsAsync(
        List<string> uuids,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.nodes_by_uuids");
    }

    // TODO(port): GraphReader.episodes_by_uuids
    internal virtual Task<List<EpisodicNode>> EpisodesByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.episodes_by_uuids");
    }

    // TODO(port): GraphReader.edges_by_uuids
    internal virtual Task<List<EntityEdge>> EdgesByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphReader.edges_by_uuids");
    }
}
