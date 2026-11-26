// Ported from graphiti_core/edges.py
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

namespace Graphiti.Core;

using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.PyCompat;

// PORT: Edge defines __eq__ and __hash__, so it and its subclasses are classes, not records (R7). Edge has no
// validate_assignment and no validator, so each set accessor is plain.
internal abstract class Edge
{
    internal string Uuid { get; set; } = Py.Uuid4();

    internal required string GroupId { get; set; }

    internal required string SourceNodeUuid { get; set; }

    internal required string TargetNodeUuid { get; set; }

    internal required PyDateTime CreatedAt { get; set; }

    internal abstract Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default);

    // TODO(port): Edge.delete
    internal virtual Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Edge.delete");
    }

    // PORT: a classmethod is a static method (R16). The cls that upstream passes to the graph operations interface is
    // typeof(Edge) here: the one upstream caller (graphiti.py:2737) calls it on Edge.
    // TODO(port): Edge.delete_by_uuids
    internal static Task DeleteByUuidsAsync(GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Edge.delete_by_uuids");
    }

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);

    // PORT: upstream defect U12. The test is for Node, so an edge never equals an edge, also not itself (R18, R21).
    public override bool Equals(object? obj) => obj is Node other && Uuid == other.Uuid;

    // PORT: Edge.get_by_uuid has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.
}

internal sealed class EpisodicEdge : Edge
{
    internal EpisodicEdge Validate() => this;

    // TODO(port): EpisodicEdge.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdge.save");
    }

    // TODO(port): EpisodicEdge.get_by_uuid
    internal static Task<EpisodicEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdge.get_by_uuid");
    }

    // TODO(port): EpisodicEdge.get_by_uuids
    internal static Task<List<EpisodicEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdge.get_by_uuids");
    }

    // TODO(port): EpisodicEdge.get_by_group_ids
    internal static Task<List<EpisodicEdge>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdge.get_by_group_ids");
    }
}

internal sealed class EntityEdge : Edge
{
    internal required string Name { get; set; }

    internal required string Fact { get; set; }

    internal List<double>? FactEmbedding { get; set; }

    internal List<string> Episodes { get; set; } = [];

    internal PyDateTime? ExpiredAt { get; set; }

    internal PyDateTime? ValidAt { get; set; }

    internal PyDateTime? InvalidAt { get; set; }

    internal PyDateTime? ReferenceTime { get; set; }

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    internal EntityEdge Validate() => this;

    // TODO(port): EntityEdge.generate_embedding
    internal Task<List<double>> GenerateEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.generate_embedding");
    }

    // TODO(port): EntityEdge.load_fact_embedding
    internal Task LoadFactEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.load_fact_embedding");
    }

    // TODO(port): EntityEdge.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.save");
    }

    // TODO(port): EntityEdge.get_by_uuid
    internal static Task<EntityEdge> GetByUuidAsync(
        GraphDriver driver,
        string uuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.get_by_uuid");
    }

    // TODO(port): EntityEdge.get_between_nodes
    internal static Task<List<EntityEdge>> GetBetweenNodesAsync(
        GraphDriver driver,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.get_between_nodes");
    }

    // TODO(port): EntityEdge.get_by_uuids
    internal static Task<List<EntityEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.get_by_uuids");
    }

    // TODO(port): EntityEdge.get_by_group_ids
    internal static Task<List<EntityEdge>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.get_by_group_ids");
    }

    // TODO(port): EntityEdge.get_by_node_uuid
    internal static Task<List<EntityEdge>> GetByNodeUuidAsync(
        GraphDriver driver,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdge.get_by_node_uuid");
    }
}

internal sealed class CommunityEdge : Edge
{
    internal CommunityEdge Validate() => this;

    // TODO(port): CommunityEdge.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdge.save");
    }

    // TODO(port): CommunityEdge.get_by_uuid
    internal static Task<CommunityEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdge.get_by_uuid");
    }

    // TODO(port): CommunityEdge.get_by_uuids
    internal static Task<List<CommunityEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdge.get_by_uuids");
    }

    // TODO(port): CommunityEdge.get_by_group_ids
    internal static Task<List<CommunityEdge>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdge.get_by_group_ids");
    }
}

internal sealed class HasEpisodeEdge : Edge
{
    internal HasEpisodeEdge Validate() => this;

    // TODO(port): HasEpisodeEdge.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdge.save");
    }

    // TODO(port): HasEpisodeEdge.delete
    internal override Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdge.delete");
    }

    // TODO(port): HasEpisodeEdge.get_by_uuid
    internal static Task<HasEpisodeEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdge.get_by_uuid");
    }

    // TODO(port): HasEpisodeEdge.get_by_uuids
    internal static Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdge.get_by_uuids");
    }

    // TODO(port): HasEpisodeEdge.get_by_group_ids
    internal static Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdge.get_by_group_ids");
    }
}

internal sealed class NextEpisodeEdge : Edge
{
    internal NextEpisodeEdge Validate() => this;

    // TODO(port): NextEpisodeEdge.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdge.save");
    }

    // TODO(port): NextEpisodeEdge.delete
    internal override Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdge.delete");
    }

    // TODO(port): NextEpisodeEdge.get_by_uuid
    internal static Task<NextEpisodeEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdge.get_by_uuid");
    }

    // TODO(port): NextEpisodeEdge.get_by_uuids
    internal static Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdge.get_by_uuids");
    }

    // TODO(port): NextEpisodeEdge.get_by_group_ids
    internal static Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdge.get_by_group_ids");
    }
}

internal static class Edges
{
    // Edge helpers
    // TODO(port): get_episodic_edge_from_record
    internal static EpisodicEdge GetEpisodicEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_episodic_edge_from_record");
    }

    // TODO(port): get_entity_edge_from_record
    internal static EntityEdge GetEntityEdgeFromRecord(OrderedDictionary<string, object?> record, GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_entity_edge_from_record");
    }

    // TODO(port): get_community_edge_from_record
    internal static CommunityEdge GetCommunityEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_community_edge_from_record");
    }

    // TODO(port): get_has_episode_edge_from_record
    internal static HasEpisodeEdge GetHasEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_has_episode_edge_from_record");
    }

    // TODO(port): get_next_episode_edge_from_record
    internal static NextEpisodeEdge GetNextEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_next_episode_edge_from_record");
    }

    // TODO(port): create_entity_edge_embeddings
    internal static Task CreateEntityEdgeEmbeddingsAsync(
        EmbedderClient embedder,
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): create_entity_edge_embeddings");
    }
}
