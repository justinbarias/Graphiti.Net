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

using System.Text.RegularExpressions;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class Nodes
{
    // PORT: R5 puts the logger on the module class. EpisodeTypeExtensions is a separate class (R7), so the logger is internal.
    internal static ILogger Logger => PortServices.Logger(typeof(Nodes));

    internal const long NodeDedupCandidateLimit = 15;

    // PORT: SAFE_CYPHER_IDENTIFIER_PATTERN.fullmatch(...) at nodes.py:681 is a second static Regex with \A(?:pattern)\z (R13).
    // Python: re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$') in helpers.py, called with fullmatch.
    internal static readonly Regex SafeCypherIdentifierPatternFullmatch = new(@"\A(?:^[A-Za-z_][A-Za-z0-9_]*$)\z", RegexOptions.CultureInvariant);
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
    // Pydantic stores a copy of the list at construction and at assignment (R7).
    internal List<string> Labels
    {
        get => labels;
        set => labels = ValidateLabels([.. value], "labels");
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

    internal virtual async Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NodeDeleteAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        switch (driver.Provider)
        {
            case GraphProvider.Neo4j:
                var (records, _, _) = await driver.ExecuteQueryAsync(
                    "\n" + """
                    MATCH (n {uuid: $uuid})
                    WHERE n:Entity OR n:Episodic OR n:Community
                    OPTIONAL MATCH (n)-[r]-()
                    WITH collect(r.uuid) AS edge_uuids, n
                    DETACH DELETE n
                    RETURN edge_uuids
""" + "\n                    ",
                    new OrderedDictionary<string, object?> { ["uuid"] = Uuid },
                    cancellationToken);
                break;

            default: // FalkorDB, Neptune
                foreach (var label in new List<string> { "Entity", "Episodic", "Community" })
                {
                    await driver.ExecuteQueryAsync(
                        "\n" + $$"""
                        MATCH (n:{{label}} {uuid: $uuid})
                        DETACH DELETE n
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["uuid"] = Uuid },
                        cancellationToken);
                }

                break;
        }

        Nodes.Log.DeletedNode(Nodes.Logger, Uuid);
    }

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Node other && Uuid == other.Uuid;

    // PORT: a classmethod is a static method (R16). The cls that upstream passes to the graph operations interface is
    // typeof(Node) here: no upstream caller calls it on a subclass, and a C# static method does not know the class
    // name of its call.
    internal static async Task DeleteByGroupIdAsync(
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NodeDeleteByGroupIdAsync(typeof(Node), driver, groupId, batchSize, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        switch (driver.Provider)
        {
            case GraphProvider.Neo4j:
                await using (var session = driver.Session())
                {
                    await session.RunAsync(
                        "\n" + """
                        MATCH (n:Entity|Episodic|Community {group_id: $group_id})
                        CALL (n) {
                            DETACH DELETE n
                        } IN TRANSACTIONS OF $batch_size ROWS
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["group_id"] = groupId, ["batch_size"] = batchSize },
                        cancellationToken);
                }

                break;

            default: // FalkorDB, Neptune
                foreach (var label in new List<string> { "Entity", "Episodic", "Community" })
                {
                    await driver.ExecuteQueryAsync(
                        "\n" + $$"""
                        MATCH (n:{{label}} {group_id: $group_id})
                        DETACH DELETE n
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["group_id"] = groupId },
                        cancellationToken);
                }

                break;
        }
    }

    internal static async Task DeleteByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NodeDeleteByUuidsAsync(typeof(Node), driver, uuids, groupId: null, batchSize: batchSize, cancellationToken: cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        switch (driver.Provider)
        {
            case GraphProvider.Falkordb:
                foreach (var label in new List<string> { "Entity", "Episodic", "Community" })
                {
                    await driver.ExecuteQueryAsync(
                        "\n" + $$"""
                        MATCH (n:{{label}})
                        WHERE n.uuid IN $uuids
                        DETACH DELETE n
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["uuids"] = uuids },
                        cancellationToken);
                }

                break;
            default: // Neo4J, Neptune
                await using (var session = driver.Session())
                {
                    // Collect all edge UUIDs before deleting nodes
                    await session.RunAsync(
                        "\n" + """
                        MATCH (n:Entity|Episodic|Community)
                        WHERE n.uuid IN $uuids
                        MATCH (n)-[r]-()
                        RETURN collect(r.uuid) AS edge_uuids
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["uuids"] = uuids },
                        cancellationToken);

                    // Now delete the nodes in batches
                    await session.RunAsync(
                        "\n" + """
                        MATCH (n:Entity|Episodic|Community)
                        WHERE n.uuid IN $uuids
                        CALL (n) {
                            DETACH DELETE n
                        } IN TRANSACTIONS OF $batch_size ROWS
""" + "\n                        ",
                        new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["batch_size"] = batchSize },
                        cancellationToken);
                }

                break;
        }
    }

    // PORT: Node.get_by_uuid has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.

    // PORT: Node.get_by_uuids has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.
}

internal sealed class EpisodicNode : Node, IPydanticModel
{
    private List<string> entityEdges = [];

    private OrderedDictionary<string, object?>? episodeMetadata;

    internal required EpisodeType Source { get; set; }

    internal required string SourceDescription { get; set; }

    internal required string Content { get; set; }

    internal required PyDateTime ValidAt { get; set; }

    // PORT: validate_assignment stores a copy of a list or a dict value (R7).
    internal List<string> EntityEdges
    {
        get => entityEdges;
        set => entityEdges = [.. value];
    }

    internal OrderedDictionary<string, object?>? EpisodeMetadata
    {
        get => episodeMetadata;
        set => episodeMetadata = value is null ? null : new(value);
    }

    internal override string PythonName => "EpisodicNode";

    internal EpisodicNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    internal static EpisodicNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EpisodicNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);

        // PORT: validate_labels is an instance method (R7), so the set accessor of Labels runs it when the node is built.
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, DatetimeUtils.UtcNow);
        var source = f.Required("source", Pydantic.Enum<EpisodeType>);
        var sourceDescription = f.Required("source_description", Pydantic.Str);
        var content = f.Required("content", Pydantic.Str);
        var validAt = f.Required("valid_at", Pydantic.DateTime);
        var entityEdges = f.Default("entity_edges", v => Pydantic.List(v, Pydantic.Str), () => []);
        var episodeMetadata = f.Default("episode_metadata", v => Pydantic.Optional(v, x => Pydantic.Dict(x, Pydantic.Any)), () => null);
        f.ThrowIfAny();
        return new EpisodicNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            Source = source.Value,
            SourceDescription = sourceDescription.Value,
            Content = content.Value,
            ValidAt = validAt.Value,
            EntityEdges = entityEdges.Value,
            EpisodeMetadata = episodeMetadata.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("source", Source),
        ("source_description", SourceDescription),
        ("content", Content),
        ("valid_at", ValidAt),
        ("entity_edges", EntityEdges),
        ("episode_metadata", EpisodeMetadata));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EpisodicNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Required("source", PydanticType.Enum<EpisodeType>(
            """
            Enumeration of different types of episodes that can be processed.

            This enum defines the various sources or formats of episodes that the system
            can handle. It's used to categorize and potentially handle different types
            of input data differently.

            Attributes:
            -----------
            message : str
                Represents a standard message-type episode. The content for this type
                should be formatted as "actor: content". For example, "user: Hello, how are you?"
                or "assistant: I'm doing well, thank you for asking."
            json : str
                Represents an episode containing a JSON string object with structured data.
            text : str
                Represents a plain text episode.
            """), "source type")
        .Required("source_description", PydanticType.Str, "description of the data source")
        .Required("content", PydanticType.Str, "raw episode data")
        .Required("valid_at", PydanticType.DateTime, "datetime of when the original document was created")
        .Factory("entity_edges", PydanticType.List(PydanticType.Str), "list of entity edges referenced in this episode")
        .Default("episode_metadata", PydanticType.Optional(PydanticType.Dict(PydanticType.Any)), null, "customer-defined metadata key-value pairs for filtering");

    internal EpisodicNode ModelCopy(bool deep = false)
    {
        var copy = (EpisodicNode)MemberwiseClone();
        if (deep)
        {
            copy.Labels = [.. Labels];
            copy.EntityEdges = [.. EntityEdges];
            copy.EpisodeMetadata = EpisodeMetadata is null ? null : Py.AsDict(Py.DeepCopy(EpisodeMetadata));
        }

        return copy;
    }

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EpisodicNodeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var episodeArgs = new OrderedDictionary<string, object?>
        {
            ["uuid"] = Uuid,
            ["name"] = Name,
            ["group_id"] = GroupId,
            ["source_description"] = SourceDescription,
            ["content"] = Content,
            ["entity_edges"] = EntityEdges,
            ["created_at"] = CreatedAt,
            ["valid_at"] = ValidAt,
            ["source"] = Py.EnumValue(Source),
        };

        var result = await driver.ExecuteQueryAsync(
            NodeDbQueries.GetEpisodeNodeSaveQuery(driver.Provider), episodeArgs, cancellationToken);

        Nodes.Log.SavedNodeToGraph(Nodes.Logger, Uuid);
    }

    /// <summary>Get an episode by UUID.
    ///
    /// Parameters
    /// ----------
    /// driver : GraphDriver
    ///     The graph driver.
    /// uuid : str
    ///     The episode UUID.
    /// group_id : str | None
    ///     If set, the episode must belong to this group or NodeNotFoundError is raised.
    ///     If None, the lookup does not check the group.
    ///
    /// Raises
    /// ------
    /// NodeNotFoundError
    ///     If the episode is not found in the selected group.</summary>
    internal static async Task<EpisodicNode> GetByUuidAsync(
        GraphDriver driver,
        string uuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            // PORT: try, except, else. The else block runs when the call returned, which the variable shows (Part 3).
            EpisodicNode? episode = null;
            try
            {
                episode = await driver.GraphOperationsInterface.EpisodicNodeGetByUuidAsync(typeof(EpisodicNode), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }

            if (episode is not null)
            {
                if (groupId is not null && episode.GroupId != groupId)
                {
                    throw new NodeNotFoundException(uuid);
                }

                return episode;
            }
        }

        var groupFilter = groupId is not null ? "WHERE e.group_id = $group_id" : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (e:Episodic {uuid: $uuid})
""" + "\n            "
            + groupFilter
            + "\n" + """
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.EpisodicNodeReturnNeptune
                : NodeDbQueries.EpisodicNodeReturn),
            new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["group_id"] = groupId, ["routing_"] = "r" },
            cancellationToken);

        var episodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();
        if (groupId is not null)
        {
            episodes = episodes.Where(episode => episode.GroupId == groupId).ToList();
        }

        if (episodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return episodes[0];
    }

    // PORT: require_all is keyword-only in Python. Signature has no mark for it (R22, O11).
    /// <summary>Get episodes by UUID.
    ///
    /// Parameters
    /// ----------
    /// driver : GraphDriver
    ///     The graph driver.
    /// uuids : list[str]
    ///     The episode UUIDs.
    /// group_id : str | None
    ///     If set, episodes in other groups are excluded. If None, no group check is performed.
    /// require_all : bool
    ///     If True, raise NodeNotFoundError for the first UUID not found in the selected group.
    ///     If False, skip missing UUIDs.</summary>
    internal static async Task<List<EpisodicNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        bool requireAll = false,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            // PORT: try, except, else. The else block runs when the call returned, which the variable shows (Part 3).
            List<EpisodicNode>? found = null;
            try
            {
                found = await driver.GraphOperationsInterface.EpisodicNodeGetByUuidsAsync(typeof(EpisodicNode), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }

            if (found is not null)
            {
                if (groupId is not null)
                {
                    found = found.Where(episode => episode.GroupId == groupId).ToList();
                }

                if (requireAll)
                {
                    var foundUuids = found.Select(episode => episode.Uuid).ToHashSet(StringComparer.Ordinal);
                    foreach (var uuid in uuids)
                    {
                        if (!foundUuids.Contains(uuid))
                        {
                            throw new NodeNotFoundException(uuid);
                        }
                    }
                }

                return found;
            }
        }

        var groupFilter = groupId is not null ? "AND e.group_id = $group_id" : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (e:Episodic)
            WHERE e.uuid IN $uuids
""" + "\n            "
            + groupFilter
            + "\n" + """
            RETURN DISTINCT
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.EpisodicNodeReturnNeptune
                : NodeDbQueries.EpisodicNodeReturn),
            new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["group_id"] = groupId, ["routing_"] = "r" },
            cancellationToken);

        var episodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();
        if (groupId is not null)
        {
            episodes = episodes.Where(episode => episode.GroupId == groupId).ToList();
        }

        if (requireAll)
        {
            var foundUuids = episodes.Select(episode => episode.Uuid).ToHashSet(StringComparer.Ordinal);
            foreach (var uuid in uuids)
            {
                if (!foundUuids.Contains(uuid))
                {
                    throw new NodeNotFoundException(uuid);
                }
            }
        }

        return episodes;
    }

    internal static async Task<List<EpisodicNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EpisodicNodeGetByGroupIdsAsync(typeof(EpisodicNode), driver, groupIds, limit, uuidCursor, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var cursorQuery = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitQuery = limit is not null ? "LIMIT $limit" : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (e:Episodic)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN DISTINCT
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.EpisodicNodeReturnNeptune
                : NodeDbQueries.EpisodicNodeReturn)
            + "\n" + """
            ORDER BY uuid DESC
""" + "\n            "
            + limitQuery,
            new OrderedDictionary<string, object?> { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var episodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();

        return episodes;
    }

    internal static async Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        GraphDriver driver,
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EpisodicNodeGetByEntityNodeUuidAsync(typeof(EpisodicNode), driver, entityNodeUuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (e:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $entity_node_uuid})
            RETURN DISTINCT
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.EpisodicNodeReturnNeptune
                : NodeDbQueries.EpisodicNodeReturn),
            new OrderedDictionary<string, object?> { ["entity_node_uuid"] = entityNodeUuid, ["routing_"] = "r" },
            cancellationToken);

        var episodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();

        return episodes;
    }
}

internal sealed class EntityNode : Node, IPydanticModel
{
    private List<double>? nameEmbedding;

    private OrderedDictionary<string, object?> attributes = new();

    // PORT: validate_assignment stores a copy of a list or a dict value (R7).
    internal List<double>? NameEmbedding
    {
        get => nameEmbedding;
        set => nameEmbedding = value is null ? null : [.. value];
    }

    internal string Summary { get; set; } = string.Empty;

    internal OrderedDictionary<string, object?> Attributes
    {
        get => attributes;
        set => attributes = new(value);
    }

    internal override string PythonName => "EntityNode";

    internal EntityNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    internal static EntityNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EntityNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);

        // PORT: validate_labels is an instance method (R7), so the set accessor of Labels runs it when the node is built.
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, DatetimeUtils.UtcNow);
        var nameEmbedding = f.Default("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)), () => null);
        var summary = f.Default("summary", Pydantic.Str, () => string.Empty);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => new OrderedDictionary<string, object?>());
        f.ThrowIfAny();
        return new EntityNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            NameEmbedding = nameEmbedding.Value,
            Summary = summary.Value,
            Attributes = attributes.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("name_embedding", NameEmbedding),
        ("summary", Summary),
        ("attributes", Attributes));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EntityNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("name_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the name")
        .Factory("summary", PydanticType.Str, "regional summary of surrounding edges")
        .Default("attributes", PydanticType.Dict(PydanticType.Any), new OrderedDictionary<string, object?>(), "Additional attributes of the node. Dependent on node labels");

    internal EntityNode ModelCopy(bool deep = false)
    {
        var copy = (EntityNode)MemberwiseClone();
        if (deep)
        {
            copy.Labels = [.. Labels];
            copy.NameEmbedding = NameEmbedding is null ? null : [.. NameEmbedding];
            copy.Attributes = Py.AsDict(Py.DeepCopy(Attributes));
        }

        return copy;
    }

    internal async Task<List<double>> GenerateNameEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();
        var text = Name.Replace("\n", " ", StringComparison.Ordinal);
        NameEmbedding = await embedder.CreateAsync([text], cancellationToken);
        var end = Py.TimeSeconds();
        var logger = Nodes.Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var length = Py.Len(text);
            var milliseconds = Py.Str((end - start) * 1000);
            Nodes.Log.EmbeddedEntityName(logger, Uuid, length, milliseconds);
        }

        // PORT: the set accessor stores a copy of the list, as pydantic does, and never null here.
        return NameEmbedding ?? [];
    }

    internal async Task LoadNameEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NodeLoadEmbeddingsAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                MATCH (n:Entity {uuid: $uuid})
                RETURN [x IN split(n.name_embedding, ",") | toFloat(x)] as name_embedding
""" + "\n            ";
        }
        else
        {
            query = "\n" + """
                MATCH (n:Entity {uuid: $uuid})
                RETURN n.name_embedding AS name_embedding
""" + "\n            ";
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?> { ["uuid"] = Uuid, ["routing_"] = "r" },
            cancellationToken);

        if (records.Count == 0)
        {
            throw new NodeNotFoundException(Uuid);
        }

        // PORT: validate_assignment validates a value from open data under the class name and the field name (R7).
        var f = new PydanticFields("EntityNode", new() { ["name_embedding"] = records[0]["name_embedding"] });
        var nameEmbedding = f.Required("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)));
        f.ThrowIfAny();
        NameEmbedding = nameEmbedding.Value;
    }

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NodeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var entityData = new OrderedDictionary<string, object?>
        {
            ["uuid"] = Uuid,
            ["name"] = Name,
            ["name_embedding"] = NameEmbedding,
            ["group_id"] = GroupId,
            ["summary"] = Summary,
            ["created_at"] = CreatedAt,
        };

        foreach (var (k, v) in Attributes)
        {
            if (!entityData.ContainsKey(k))
            {
                entityData[k] = v;
            }
        }

        var labels = string.Join(':', [.. Labels, "Entity"]);

        var result = await driver.ExecuteQueryAsync(
            NodeDbQueries.GetEntityNodeSaveQuery(driver.Provider, labels),
            new OrderedDictionary<string, object?> { ["entity_data"] = entityData },
            cancellationToken);

        Nodes.Log.SavedNodeToGraph(Nodes.Logger, Uuid);
    }

    internal static async Task<EntityNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NodeGetByUuidAsync(typeof(EntityNode), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Entity {uuid: $uuid})
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider),
            new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal static async Task<List<EntityNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NodeGetByUuidsAsync(typeof(EntityNode), driver, uuids, groupId, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider),
            new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    internal static async Task<List<EntityNode>> GetByNamesAsync(
        GraphDriver driver,
        List<string> names,
        List<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        if (names.Count == 0 || groupIds.Count == 0)
        {
            return [];
        }

        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NodeGetByNamesAsync(typeof(EntityNode), driver, names, groupIds, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // PORT: sorted() of a set: a HashSet, then the sort by code point (R14).
        var uniqueNames = names.Where(name => !string.IsNullOrEmpty(name)).ToHashSet(StringComparer.Ordinal).Order(Py.StringComparer).ToList();
        var uniqueGroupIds = groupIds.Where(groupId => !string.IsNullOrEmpty(groupId)).ToHashSet(StringComparer.Ordinal).Order(Py.StringComparer).ToList();
        if (uniqueNames.Count == 0 || uniqueGroupIds.Count == 0)
        {
            return [];
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Entity)
            WHERE n.group_id IN $group_ids
            AND n.name IN $names
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider),
            new OrderedDictionary<string, object?> { ["names"] = uniqueNames, ["group_ids"] = uniqueGroupIds, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    /// <summary>Retrieve nodes by attributes. Neptune does not support this lookup.</summary>
    internal static async Task<List<EntityNode>> GetByAttributeValuesAsync(
        GraphDriver driver,
        string groupId,
        OrderedDictionary<string, object?> attributes,
        List<string>? labels = null,
        long limit = Nodes.NodeDedupCandidateLimit,
        CancellationToken cancellationToken = default)
    {
        if (attributes.Count == 0)
        {
            return [];
        }

        var propertyNames = attributes.Keys.ToList();
        foreach (var propertyName in propertyNames)
        {
            if (!Nodes.SafeCypherIdentifierPatternFullmatch.IsMatch(propertyName))
            {
                throw new ArgumentException("Invalid attribute name: " + propertyName);
            }
        }

        var nodeLabels = new List<string>(labels is { Count: > 0 } ? labels : []);
        Helpers.ValidateNodeLabels(nodeLabels);
        var filteredLabels = nodeLabels.Where(label => label != "Entity").ToList();
        var labelQuery = string.Join(string.Empty, filteredLabels.Select(label => ":" + label));

        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NodeGetByAttributeValuesAsync(
                    typeof(EntityNode),
                    driver,
                    groupId,
                    attributes,
                    nodeLabels,
                    limit,
                    cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        if (driver.Provider is not (GraphProvider.Neo4j or GraphProvider.Falkordb))
        {
            throw new NotImplementedException("Attribute lookup is not supported for " + Py.Str(Py.EnumValue(driver.Provider)));
        }

        var attributeConditions = propertyNames
            .Select((propertyName, index) => "AND n.`" + propertyName + "` = $attribute_" + Py.Str((long)index))
            .ToList();
        var query =
            "\n" + $$"""
            MATCH (n:Entity{{labelQuery}})
            WHERE n.group_id = $group_id
            {{string.Join(' ', attributeConditions)}}
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider)
            + "\n" + """
            LIMIT $limit
""" + "\n            ";
        var parameters = new OrderedDictionary<string, object?>();
        foreach (var (index, propertyName) in propertyNames.Index())
        {
            parameters["attribute_" + Py.Str((long)index)] = attributes[propertyName];
        }

        parameters["group_id"] = groupId;
        parameters["limit"] = limit;
        if (driver.Provider == GraphProvider.Neo4j)
        {
            parameters["routing_"] = "r";
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        return records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();
    }

    internal static async Task<List<EntityNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NodeGetByGroupIdsAsync(typeof(EntityNode), driver, groupIds, limit, uuidCursor, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var cursorQuery = !string.IsNullOrEmpty(uuidCursor) ? "AND n.uuid < $uuid" : string.Empty;
        var limitQuery = limit is not null ? "LIMIT $limit" : string.Empty;
        var withEmbeddingsQuery = withEmbeddings
            ? ",\n" + """
            n.name_embedding AS name_embedding
""" + "\n            "
            : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Entity)
            WHERE n.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider)
            + withEmbeddingsQuery
            + "\n" + """
            ORDER BY n.uuid DESC
""" + "\n            "
            + limitQuery,
            new OrderedDictionary<string, object?> { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }
}

internal sealed class CommunityNode : Node, IPydanticModel
{
    private List<double>? nameEmbedding;

    // PORT: validate_assignment stores a copy of a list value (R7).
    internal List<double>? NameEmbedding
    {
        get => nameEmbedding;
        set => nameEmbedding = value is null ? null : [.. value];
    }

    internal string Summary { get; set; } = string.Empty;

    internal override string PythonName => "CommunityNode";

    internal CommunityNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }

    internal static CommunityNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CommunityNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);

        // PORT: validate_labels is an instance method (R7), so the set accessor of Labels runs it when the node is built.
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, DatetimeUtils.UtcNow);
        var nameEmbedding = f.Default("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)), () => null);
        var summary = f.Default("summary", Pydantic.Str, () => string.Empty);
        f.ThrowIfAny();
        return new CommunityNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            NameEmbedding = nameEmbedding.Value,
            Summary = summary.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("name_embedding", NameEmbedding),
        ("summary", Summary));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CommunityNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("name_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the name")
        .Factory("summary", PydanticType.Str, "region summary of member nodes");

    internal CommunityNode ModelCopy(bool deep = false)
    {
        var copy = (CommunityNode)MemberwiseClone();
        if (deep)
        {
            copy.Labels = [.. Labels];
            copy.NameEmbedding = NameEmbedding is null ? null : [.. NameEmbedding];
        }

        return copy;
    }

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.CommunityNodeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        if (driver.Provider == GraphProvider.Neptune)
        {
            // TODO(port): no rule for driver.save_to_aoss. Only the Neptune driver has the method, and GraphDriver has no member for it (D10).
            throw new NotSupportedException("TODO(port): no rule for driver.save_to_aoss");
        }

        var result = await driver.ExecuteQueryAsync(
            NodeDbQueries.GetCommunityNodeSaveQuery(driver.Provider),
            new OrderedDictionary<string, object?>
            {
                ["uuid"] = Uuid,
                ["name"] = Name,
                ["group_id"] = GroupId,
                ["summary"] = Summary,
                ["name_embedding"] = NameEmbedding,
                ["created_at"] = CreatedAt,
            },
            cancellationToken);

        Nodes.Log.SavedNodeToGraph(Nodes.Logger, Uuid);
    }

    internal async Task<List<double>> GenerateNameEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();
        var text = Name.Replace("\n", " ", StringComparison.Ordinal);
        NameEmbedding = await embedder.CreateAsync([text], cancellationToken);
        var end = Py.TimeSeconds();
        var logger = Nodes.Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var length = Py.Len(text);
            var milliseconds = Py.Str((end - start) * 1000);
            Nodes.Log.EmbeddedEntityName(logger, Uuid, length, milliseconds);
        }

        // PORT: the set accessor stores a copy of the list, as pydantic does, and never null here.
        return NameEmbedding ?? [];
    }

    internal async Task LoadNameEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.CommunityNodeLoadNameEmbeddingAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                MATCH (c:Community {uuid: $uuid})
                RETURN [x IN split(c.name_embedding, ",") | toFloat(x)] as name_embedding
""" + "\n            ";
        }
        else
        {
            query = "\n" + """
            MATCH (c:Community {uuid: $uuid})
            RETURN c.name_embedding AS name_embedding
""" + "\n            ";
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?> { ["uuid"] = Uuid, ["routing_"] = "r" },
            cancellationToken);

        if (records.Count == 0)
        {
            throw new NodeNotFoundException(Uuid);
        }

        // PORT: validate_assignment validates a value from open data under the class name and the field name (R7).
        var f = new PydanticFields("CommunityNode", new() { ["name_embedding"] = records[0]["name_embedding"] });
        var nameEmbedding = f.Required("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)));
        f.ThrowIfAny();
        NameEmbedding = nameEmbedding.Value;
    }

    internal static async Task<CommunityNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.CommunityNodeGetByUuidAsync(typeof(CommunityNode), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community {uuid: $uuid})
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.CommunityNodeReturnNeptune
                : NodeDbQueries.CommunityNodeReturn),
            new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal static async Task<List<CommunityNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.CommunityNodeGetByUuidsAsync(typeof(CommunityNode), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community)
            WHERE c.uuid IN $uuids
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.CommunityNodeReturnNeptune
                : NodeDbQueries.CommunityNodeReturn),
            new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var communities = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        return communities;
    }

    internal static async Task<List<CommunityNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.CommunityNodeGetByGroupIdsAsync(typeof(CommunityNode), driver, groupIds, limit, uuidCursor, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var cursorQuery = !string.IsNullOrEmpty(uuidCursor) ? "AND c.uuid < $uuid" : string.Empty;
        var limitQuery = limit is not null ? "LIMIT $limit" : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community)
            WHERE c.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.CommunityNodeReturnNeptune
                : NodeDbQueries.CommunityNodeReturn)
            + "\n" + """
            ORDER BY c.uuid DESC
""" + "\n            "
            + limitQuery,
            new OrderedDictionary<string, object?> { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var communities = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        return communities;
    }
}

internal sealed class SagaNode : Node, IPydanticModel
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

    internal static SagaNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SagaNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);

        // PORT: validate_labels is an instance method (R7), so the set accessor of Labels runs it when the node is built.
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, DatetimeUtils.UtcNow);
        var summary = f.Default("summary", Pydantic.Str, () => string.Empty);
        var firstEpisodeUuid = f.Default("first_episode_uuid", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var lastEpisodeUuid = f.Default("last_episode_uuid", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var lastSummarizedAt = f.Default("last_summarized_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var lastSummarizedEpisodeValidAt = f.Default("last_summarized_episode_valid_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        f.ThrowIfAny();
        return new SagaNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            Summary = summary.Value,
            FirstEpisodeUuid = firstEpisodeUuid.Value,
            LastEpisodeUuid = lastEpisodeUuid.Value,
            LastSummarizedAt = lastSummarizedAt.Value,
            LastSummarizedEpisodeValidAt = lastSummarizedEpisodeValidAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("summary", Summary),
        ("first_episode_uuid", FirstEpisodeUuid),
        ("last_episode_uuid", LastEpisodeUuid),
        ("last_summarized_at", LastSummarizedAt),
        ("last_summarized_episode_valid_at", LastSummarizedEpisodeValidAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SagaNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("summary", PydanticType.Str, string.Empty)
        .Default("first_episode_uuid", PydanticType.Optional(PydanticType.Str), null)
        .Default("last_episode_uuid", PydanticType.Optional(PydanticType.Str), null)
        .Default("last_summarized_at", PydanticType.Optional(PydanticType.DateTime), null)
        .Default("last_summarized_episode_valid_at", PydanticType.Optional(PydanticType.DateTime), null);

    internal SagaNode ModelCopy(bool deep = false)
    {
        var copy = (SagaNode)MemberwiseClone();
        if (deep)
        {
            copy.Labels = [.. Labels];
        }

        return copy;
    }

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.SagaNodeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var result = await driver.ExecuteQueryAsync(
            NodeDbQueries.GetSagaNodeSaveQuery(driver.Provider),
            new OrderedDictionary<string, object?>
            {
                ["uuid"] = Uuid,
                ["name"] = Name,
                ["group_id"] = GroupId,
                ["created_at"] = CreatedAt,
                ["summary"] = Summary,
                ["first_episode_uuid"] = FirstEpisodeUuid,
                ["last_episode_uuid"] = LastEpisodeUuid,
                ["last_summarized_at"] = LastSummarizedAt,
                ["last_summarized_episode_valid_at"] = LastSummarizedEpisodeValidAt,
            },
            cancellationToken);

        Nodes.Log.SavedNodeToGraph(Nodes.Logger, Uuid);
    }

    internal override async Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.SagaNodeDeleteAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Saga {uuid: $uuid})
            DETACH DELETE n
""" + "\n            ",
            new OrderedDictionary<string, object?> { ["uuid"] = Uuid },
            cancellationToken);

        Nodes.Log.DeletedNode(Nodes.Logger, Uuid);
    }

    internal static async Task<SagaNode> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.SagaNodeGetByUuidAsync(typeof(SagaNode), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (s:Saga {uuid: $uuid})
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.SagaNodeReturnNeptune
                : NodeDbQueries.SagaNodeReturn),
            new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(Nodes.GetSagaNodeFromRecord).ToList();

        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal static async Task<List<SagaNode>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.SagaNodeGetByUuidsAsync(typeof(SagaNode), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (s:Saga)
            WHERE s.uuid IN $uuids
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.SagaNodeReturnNeptune
                : NodeDbQueries.SagaNodeReturn),
            new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var sagas = records.Select(Nodes.GetSagaNodeFromRecord).ToList();

        return sagas;
    }

    internal static async Task<List<SagaNode>> GetByGroupIdsAsync(
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.SagaNodeGetByGroupIdsAsync(typeof(SagaNode), driver, groupIds, limit, uuidCursor, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var cursorQuery = !string.IsNullOrEmpty(uuidCursor) ? "AND s.uuid < $uuid" : string.Empty;
        var limitQuery = limit is not null ? "LIMIT $limit" : string.Empty;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (s:Saga)
            WHERE s.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.SagaNodeReturnNeptune
                : NodeDbQueries.SagaNodeReturn)
            + "\n" + """
            ORDER BY s.uuid DESC
""" + "\n            "
            + limitQuery,
            new OrderedDictionary<string, object?> { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var sagas = records.Select(Nodes.GetSagaNodeFromRecord).ToList();

        return sagas;
    }
}

internal static partial class Nodes
{
    // Node helpers
    internal static EpisodicNode GetEpisodicNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        var createdAt = Helpers.ParseDbDate(record["created_at"]);
        var validAt = Helpers.ParseDbDate(record["valid_at"]);

        if (createdAt is null)
        {
            throw new ArgumentException("created_at cannot be None for episode " + Py.Str(Py.Get(record, "uuid", (object?)"unknown")));
        }

        if (validAt is null)
        {
            throw new ArgumentException("valid_at cannot be None for episode " + Py.Str(Py.Get(record, "uuid", (object?)"unknown")));
        }

        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return EpisodicNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["content"] = record["content"],
            ["created_at"] = createdAt,
            ["valid_at"] = validAt,
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source"] = EpisodeTypeExtensions.FromStr(Py.AsString(record["source"])),
            ["name"] = record["name"],
            ["source_description"] = record["source_description"],
            ["entity_edges"] = record["entity_edges"],
        });
    }

    internal static EntityNode GetEntityNodeFromRecord(OrderedDictionary<string, object?> record, GraphProvider provider)
    {
        var attributes = Py.AsDict(record["attributes"]);
        attributes.Remove("uuid");
        attributes.Remove("name");
        attributes.Remove("group_id");
        attributes.Remove("name_embedding");
        attributes.Remove("summary");
        attributes.Remove("created_at");
        attributes.Remove("labels");

        var labels = Py.AsList(Py.Get(record, "labels", (object?)new List<object?>()));
        var groupId = Py.Get(record, "group_id");
        if (Py.Contains(labels, "Entity_" + Py.AsString(Py.NotNone(groupId, "replace")).Replace("-", string.Empty, StringComparison.Ordinal)))
        {
            Py.Remove(labels, "Entity_" + Py.AsString(Py.NotNone(groupId, "replace")).Replace("-", string.Empty, StringComparison.Ordinal));
        }

        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        var entityNode = EntityNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["name_embedding"] = Py.Get(record, "name_embedding"),
            ["group_id"] = groupId,
            ["labels"] = labels,
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]),
            ["summary"] = record["summary"],
            ["attributes"] = attributes,
        });

        return entityNode;
    }

    internal static CommunityNode GetCommunityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return CommunityNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["name_embedding"] = record["name_embedding"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]),
            ["summary"] = record["summary"],
        });
    }

    internal static SagaNode GetSagaNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        var lastSummarizedAt = Py.Get(record, "last_summarized_at");
        var lastSummarizedEpisodeValidAt = Py.Get(record, "last_summarized_episode_valid_at");
        var summary = Py.Get(record, "summary", (object?)string.Empty);

        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return SagaNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]),
            ["summary"] = Py.Truthy(summary) ? summary : string.Empty,
            ["first_episode_uuid"] = Py.Get(record, "first_episode_uuid"),
            ["last_episode_uuid"] = Py.Get(record, "last_episode_uuid"),
            ["last_summarized_at"] = Py.Truthy(lastSummarizedAt) ? Helpers.ParseDbDate(lastSummarizedAt) : null,
            ["last_summarized_episode_valid_at"] = Py.Truthy(lastSummarizedEpisodeValidAt)
                ? Helpers.ParseDbDate(lastSummarizedEpisodeValidAt)
                : null,
        });
    }

    internal static async Task CreateEntityNodeEmbeddingsAsync(
        EmbedderClient embedder,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        // filter out falsey values from nodes
        var filteredNodes = nodes.Where(node => !string.IsNullOrEmpty(node.Name)).ToList();

        if (filteredNodes.Count == 0)
        {
            return;
        }

        var nameEmbeddings = await embedder.CreateBatchAsync(filteredNodes.Select(node => node.Name).ToList(), cancellationToken);

        // PORT: zip(strict=True) compares the counts first (Part 3).
        if (filteredNodes.Count != nameEmbeddings.Count)
        {
            throw new ArgumentException(
                nameEmbeddings.Count < filteredNodes.Count
                    ? "zip() argument 2 is shorter than argument 1"
                    : "zip() argument 2 is longer than argument 1");
        }

        foreach (var (node, nameEmbedding) in filteredNodes.Zip(nameEmbeddings))
        {
            node.NameEmbedding = nameEmbedding;
        }
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Episode type: {EpisodeType} not implemented")]
        internal static partial void EpisodeTypeNotImplemented(ILogger logger, string episodeType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Node: {Uuid}")]
        internal static partial void DeletedNode(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Node to Graph: {Uuid}")]
        internal static partial void SavedNodeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "embedded entity {Uuid} name ({Length} chars) in {Milliseconds} ms")]
        internal static partial void EmbeddedEntityName(ILogger logger, string uuid, long length, string milliseconds);
    }
}
