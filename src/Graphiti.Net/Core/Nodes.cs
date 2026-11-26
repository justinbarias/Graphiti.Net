// Ported from graphiti_core/nodes.py
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
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class Nodes
{
    // PORT: R5 puts the logger on the module class. EpisodeTypeExtensions is a separate class (R7), so the logger is internal.
    internal static ILogger Logger => PortServices.Logger(typeof(Nodes));

    internal const long NodeDedupCandidateLimit = 15;
}

/// <summary>
/// Enumeration of different types of episodes that can be processed.
///
/// This enum defines the various sources or formats of episodes that the system
/// can handle. It's used to categorize and potentially handle different types
/// of input data differently.
///
/// Attributes:
/// -----------
/// message : str
///     Represents a standard message-type episode. The content for this type
///     should be formatted as "actor: content". For example, "user: Hello, how are you?"
///     or "assistant: I'm doing well, thank you for asking."
/// json : str
///     Represents an episode containing a JSON string object with structured data.
/// text : str
///     Represents a plain text episode.
/// </summary>
internal enum EpisodeType
{
    [PyEnumMember("message", "message")]
    Message,

    [PyEnumMember("json", "json")]
    Json,

    [PyEnumMember("text", "text")]
    Text,

    [PyEnumMember("fact_triple", "fact_triple")]
    FactTriple,
}

/// <summary>The static methods of the Python class <c>EpisodeType</c> (rule R7).</summary>
internal static class EpisodeTypeExtensions
{
    internal static EpisodeType FromStr(string episodeType)
    {
        if (episodeType == "message")
        {
            return EpisodeType.Message;
        }

        if (episodeType == "json")
        {
            return EpisodeType.Json;
        }

        if (episodeType == "text")
        {
            return EpisodeType.Text;
        }

        if (episodeType == "fact_triple")
        {
            return EpisodeType.FactTriple;
        }

        Nodes.Log.EpisodeTypeNotImplemented(Nodes.Logger, episodeType);
        throw new NotImplementedException(string.Empty);
    }
}

internal abstract class Node
{
    private List<string> labels = [];

    internal string Uuid { get; set; } = Py.Uuid4();

    internal required string Name { get; set; }

    internal required string GroupId { get; set; }

    // PORT: ConfigDict(validate_assignment=True) with a validator on this field, so the set accessor validates (R7).
    internal List<string> Labels
    {
        get => labels;
        set => labels = ValidateLabels(value, "labels");
    }

    internal PyDateTime CreatedAt { get; set; } = DatetimeUtils.UtcNow();

    // PORT: the pydantic error carries the name of the class of the instance (R15).
    internal abstract string PythonName { get; }

    internal List<string> ValidateLabels(List<string> value, string? field = null)
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => Helpers.ValidateNodeLabels(value), PythonName, field);
        return value;
    }

    internal abstract Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default);

    // TODO(port): Node.delete
    internal virtual Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Node.delete");
    }

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Node other && Uuid == other.Uuid;

    // PORT: a classmethod is a static method (R16). The cls that upstream passes to the graph operations interface is
    // typeof(Node) here: no upstream caller calls it on a subclass, and a C# static method does not know the class
    // name of its call.
    // TODO(port): Node.delete_by_group_id
    internal static Task DeleteByGroupIdAsync(
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Node.delete_by_group_id");
    }

    // TODO(port): Node.delete_by_uuids
    internal static Task DeleteByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Node.delete_by_uuids");
    }

    // PORT: Node.get_by_uuid has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.

    // PORT: Node.get_by_uuids has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.
}

internal sealed class EpisodicNode : Node
{
    internal required EpisodeType Source { get; set; }

    internal required string SourceDescription { get; set; }

    internal required string Content { get; set; }

    internal required PyDateTime ValidAt { get; set; }

    internal List<string> EntityEdges { get; set; } = [];

    internal OrderedDictionary<string, object?>? EpisodeMetadata { get; set; }

    internal override string PythonName => "EpisodicNode";

    internal EpisodicNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    // TODO(port): EpisodicNode.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicNode.save");
    }

    // TODO(port): EpisodicNode.get_by_uuid
    internal static Task<EpisodicNode> GetByUuidAsync(
        GraphDriver driver,
        string uuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicNode.get_by_uuid");
    }

    // PORT: require_all is keyword-only in Python. Signature has no mark for it (R22, O11).
    // TODO(port): EpisodicNode.get_by_uuids
    internal static Task<List<EpisodicNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        bool requireAll = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicNode.get_by_uuids");
    }

    // TODO(port): EpisodicNode.get_by_group_ids
    internal static Task<List<EpisodicNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicNode.get_by_group_ids");
    }

    // TODO(port): EpisodicNode.get_by_entity_node_uuid
    internal static Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        GraphDriver driver,
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicNode.get_by_entity_node_uuid");
    }
}

internal sealed class EntityNode : Node
{
    internal List<double>? NameEmbedding { get; set; }

    internal string Summary { get; set; } = string.Empty;

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    internal override string PythonName => "EntityNode";

    internal EntityNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    // TODO(port): EntityNode.generate_name_embedding
    internal Task<List<double>> GenerateNameEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.generate_name_embedding");
    }

    // TODO(port): EntityNode.load_name_embedding
    internal Task LoadNameEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.load_name_embedding");
    }

    // TODO(port): EntityNode.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.save");
    }

    // TODO(port): EntityNode.get_by_uuid
    internal static Task<EntityNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.get_by_uuid");
    }

    // TODO(port): EntityNode.get_by_uuids
    internal static Task<List<EntityNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.get_by_uuids");
    }

    // TODO(port): EntityNode.get_by_names
    internal static Task<List<EntityNode>> GetByNamesAsync(
        GraphDriver driver,
        List<string> names,
        List<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.get_by_names");
    }

    // TODO(port): EntityNode.get_by_attribute_values
    internal static Task<List<EntityNode>> GetByAttributeValuesAsync(
        GraphDriver driver,
        string groupId,
        OrderedDictionary<string, object?> attributes,
        List<string>? labels = null,
        long limit = Nodes.NodeDedupCandidateLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.get_by_attribute_values");
    }

    // TODO(port): EntityNode.get_by_group_ids
    internal static Task<List<EntityNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNode.get_by_group_ids");
    }
}

internal sealed class CommunityNode : Node
{
    internal List<double>? NameEmbedding { get; set; }

    internal string Summary { get; set; } = string.Empty;

    internal override string PythonName => "CommunityNode";

    internal CommunityNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    // TODO(port): CommunityNode.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.save");
    }

    // TODO(port): CommunityNode.generate_name_embedding
    internal Task<List<double>> GenerateNameEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.generate_name_embedding");
    }

    // TODO(port): CommunityNode.load_name_embedding
    internal Task LoadNameEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.load_name_embedding");
    }

    // TODO(port): CommunityNode.get_by_uuid
    internal static Task<CommunityNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.get_by_uuid");
    }

    // TODO(port): CommunityNode.get_by_uuids
    internal static Task<List<CommunityNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.get_by_uuids");
    }

    // TODO(port): CommunityNode.get_by_group_ids
    internal static Task<List<CommunityNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNode.get_by_group_ids");
    }
}

internal sealed class SagaNode : Node
{
    internal string Summary { get; set; } = string.Empty;

    internal string? FirstEpisodeUuid { get; set; }

    internal string? LastEpisodeUuid { get; set; }

    internal PyDateTime? LastSummarizedAt { get; set; }

    // Maximum ``valid_at`` (episode reference time) across all episodes covered
    // by the most recent summary. ``last_summarized_at`` is the ingestion-time
    // filter watermark for the next incremental summarize run; this field
    // carries the episode-time semantics for public/temporal consumers.
    internal PyDateTime? LastSummarizedEpisodeValidAt { get; set; }

    // PORT: PrivateAttr. The four values are not model fields. graphiti.py sets them, so they are internal (R2).
    internal long SummaryEpisodesFetched { get; set; }

    internal long SummaryEpisodesSelected { get; set; }

    internal long SummaryEpisodesSkipped { get; set; }

    internal long SummarySelectedEpisodeBytes { get; set; }

    internal override string PythonName => "SagaNode";

    internal SagaNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    // TODO(port): SagaNode.save
    internal override Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNode.save");
    }

    // TODO(port): SagaNode.delete
    internal override Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNode.delete");
    }

    // TODO(port): SagaNode.get_by_uuid
    internal static Task<SagaNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNode.get_by_uuid");
    }

    // TODO(port): SagaNode.get_by_uuids
    internal static Task<List<SagaNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNode.get_by_uuids");
    }

    // TODO(port): SagaNode.get_by_group_ids
    internal static Task<List<SagaNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNode.get_by_group_ids");
    }
}

internal static partial class Nodes
{
    // Node helpers
    // TODO(port): get_episodic_node_from_record
    internal static EpisodicNode GetEpisodicNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_episodic_node_from_record");
    }

    // TODO(port): get_entity_node_from_record
    internal static EntityNode GetEntityNodeFromRecord(OrderedDictionary<string, object?> record, GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_from_record");
    }

    // TODO(port): get_community_node_from_record
    internal static CommunityNode GetCommunityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_community_node_from_record");
    }

    // TODO(port): get_saga_node_from_record
    internal static SagaNode GetSagaNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): get_saga_node_from_record");
    }

    // TODO(port): create_entity_node_embeddings
    internal static Task CreateEntityNodeEmbeddingsAsync(
        EmbedderClient embedder,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): create_entity_node_embeddings");
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Episode type: {EpisodeType} not implemented")]
        internal static partial void EpisodeTypeNotImplemented(ILogger logger, string episodeType);
    }
}
