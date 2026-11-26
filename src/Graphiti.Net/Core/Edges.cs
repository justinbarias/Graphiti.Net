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
using Graphiti.Core.Models.Edges;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class Edges
{
    // PORT: R5 puts the logger on the module class. The classes of the edge types log, so the logger is internal.
    internal static ILogger Logger => PortServices.Logger(typeof(Edges));
}

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

    internal virtual async Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EdgeDeleteAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n)-[e:MENTIONS|RELATES_TO|HAS_MEMBER {uuid: $uuid}]->(m)
            DELETE e
""" + "\n            ",
            new() { ["uuid"] = Uuid },
            cancellationToken);

        Edges.Log.DeletedEdge(Edges.Logger, Uuid);
    }

    // PORT: a classmethod is a static method (R16). The cls that upstream passes to the graph operations interface is
    // typeof(Edge) here: the one upstream caller (graphiti.py:2737) calls it on Edge.
    internal static async Task DeleteByUuidsAsync(GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EdgeDeleteByUuidsAsync(typeof(Edge), driver, uuids, cancellationToken: cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            "\n" + """
                MATCH (n)-[e:MENTIONS|RELATES_TO|HAS_MEMBER]->(m)
                WHERE e.uuid IN $uuids
                DELETE e
""" + "\n                ",
            new() { ["uuids"] = uuids },
            cancellationToken);

        var logger = Edges.Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var text = Py.Repr(uuids);
            Edges.Log.DeletedEdges(logger, text);
        }
    }

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);

    // PORT: upstream defect U12. The test is for Node, so an edge never equals an edge, also not itself (R18, R21).
    public override bool Equals(object? obj) => obj is Node other && Uuid == other.Uuid;

    // PORT: Edge.get_by_uuid has a body of "..." and each subclass replaces it. A C# static method does not override, so the base has no member.
}

internal sealed class EpisodicEdge : Edge, IPydanticModel
{
    internal EpisodicEdge Validate() => this;

    internal static EpisodicEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EpisodicEdge", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var groupId = f.Required("group_id", Pydantic.Str);
        var sourceNodeUuid = f.Required("source_node_uuid", Pydantic.Str);
        var targetNodeUuid = f.Required("target_node_uuid", Pydantic.Str);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        f.ThrowIfAny();
        return new EpisodicEdge
        {
            Uuid = uuid.Value,
            GroupId = groupId.Value,
            SourceNodeUuid = sourceNodeUuid.Value,
            TargetNodeUuid = targetNodeUuid.Value,
            CreatedAt = createdAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("group_id", GroupId),
        ("source_node_uuid", SourceNodeUuid),
        ("target_node_uuid", TargetNodeUuid),
        ("created_at", CreatedAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EpisodicEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    // PORT: model_copy(deep=True) (R7). The fields of the class are immutable, so a deep copy copies nothing more.
    internal EpisodicEdge ModelCopy(bool deep = false) => (EpisodicEdge)MemberwiseClone();

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EpisodicEdgeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            EdgeDbQueries.EpisodicEdgeSave,
            new()
            {
                ["episode_uuid"] = SourceNodeUuid,
                ["entity_uuid"] = TargetNodeUuid,
                ["uuid"] = Uuid,
                ["group_id"] = GroupId,
                ["created_at"] = CreatedAt,
            },
            cancellationToken);

        Edges.Log.SavedEdge(Edges.Logger, Uuid);
    }

    internal static async Task<EpisodicEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EpisodicEdgeGetByUuidAsync(typeof(EpisodicEdge), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Episodic)-[e:MENTIONS {uuid: $uuid}]->(m:Entity)
            RETURN
""" + "\n            "
            + EdgeDbQueries.EpisodicEdgeReturn,
            new() { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetEpisodicEdgeFromRecord).ToList();

        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal static async Task<List<EpisodicEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EpisodicEdgeGetByUuidsAsync(typeof(EpisodicEdge), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Episodic)-[e:MENTIONS]->(m:Entity)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            "
            + EdgeDbQueries.EpisodicEdgeReturn,
            new() { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetEpisodicEdgeFromRecord).ToList();

        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuids[0]);
        }

        return edges;
    }

    internal static async Task<List<EpisodicEdge>> GetByGroupIdsAsync(
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
                return await driver.GraphOperationsInterface.EpisodicEdgeGetByGroupIdsAsync(
                    typeof(EpisodicEdge),
                    driver,
                    groupIds,
                    limit,
                    uuidCursor,
                    cancellationToken);
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
            MATCH (n:Episodic)-[e:MENTIONS]->(m:Entity)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.EpisodicEdgeReturn
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitQuery,
            new() { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetEpisodicEdgeFromRecord).ToList();

        if (edges.Count == 0)
        {
            throw new GroupsEdgesNotFoundException(groupIds);
        }

        return edges;
    }
}

internal sealed class EntityEdge : Edge, IPydanticModel
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

    internal static EntityEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EntityEdge", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var groupId = f.Required("group_id", Pydantic.Str);
        var sourceNodeUuid = f.Required("source_node_uuid", Pydantic.Str);
        var targetNodeUuid = f.Required("target_node_uuid", Pydantic.Str);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        var name = f.Required("name", Pydantic.Str);
        var fact = f.Required("fact", Pydantic.Str);
        var factEmbedding = f.Default("fact_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)), () => null);
        var episodes = f.Default("episodes", v => Pydantic.List(v, Pydantic.Str), () => []);
        var expiredAt = f.Default("expired_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var validAt = f.Default("valid_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var invalidAt = f.Default("invalid_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var referenceTime = f.Default("reference_time", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => new());
        f.ThrowIfAny();
        return new EntityEdge
        {
            Uuid = uuid.Value,
            GroupId = groupId.Value,
            SourceNodeUuid = sourceNodeUuid.Value,
            TargetNodeUuid = targetNodeUuid.Value,
            CreatedAt = createdAt.Value,
            Name = name.Value,
            Fact = fact.Value,
            FactEmbedding = factEmbedding.Value,
            Episodes = episodes.Value,
            ExpiredAt = expiredAt.Value,
            ValidAt = validAt.Value,
            InvalidAt = invalidAt.Value,
            ReferenceTime = referenceTime.Value,
            Attributes = attributes.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("group_id", GroupId),
        ("source_node_uuid", SourceNodeUuid),
        ("target_node_uuid", TargetNodeUuid),
        ("created_at", CreatedAt),
        ("name", Name),
        ("fact", Fact),
        ("fact_embedding", FactEmbedding),
        ("episodes", Episodes),
        ("expired_at", ExpiredAt),
        ("valid_at", ValidAt),
        ("invalid_at", InvalidAt),
        ("reference_time", ReferenceTime),
        ("attributes", Attributes));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EntityEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime)
        .Required("name", PydanticType.Str, "name of the edge, relation name")
        .Required("fact", PydanticType.Str, "fact representing the edge and nodes that it connects")
        .Default("fact_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the fact")
        .Default("episodes", PydanticType.List(PydanticType.Str), new List<object?>(), "list of episode ids that reference these entity edges")
        .Default(
            "expired_at",
            PydanticType.Optional(PydanticType.DateTime),
            null,
            "datetime of when Graphiti marked the edge as invalidated (transaction time); "
            + "use invalid_at to find when the fact stopped being true")
        .Default(
            "valid_at",
            PydanticType.Optional(PydanticType.DateTime),
            null,
            "datetime of when the fact became true; future-tense plans/promises use the "
            + "episode reference time rather than the future occurrence date")
        .Default("invalid_at", PydanticType.Optional(PydanticType.DateTime), null, "datetime of when the fact stopped being true; soft deadlines are not invalid_at")
        .Default("reference_time", PydanticType.Optional(PydanticType.DateTime), null, "reference timestamp from the episode that produced this edge")
        .Default("attributes", PydanticType.Dict(PydanticType.Any), new OrderedDictionary<string, object?>(), "Additional attributes of the edge. Dependent on edge name");

    // PORT: model_copy(deep=True) (R7). MemberwiseClone copies each property and validates nothing. A deep copy
    // replaces each list and the dict with a copy, as Nodes.cs does.
    internal EntityEdge ModelCopy(bool deep = false)
    {
        var copy = (EntityEdge)MemberwiseClone();
        if (deep)
        {
            copy.Episodes = [.. Episodes];
            copy.FactEmbedding = FactEmbedding is null ? null : [.. FactEmbedding];
            copy.Attributes = Py.AsDict(Py.DeepCopy(Attributes));
        }

        return copy;
    }

    internal async Task<List<double>> GenerateEmbeddingAsync(EmbedderClient embedder, CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();

        var text = Fact.Replace("\n", " ", StringComparison.Ordinal);
        FactEmbedding = await embedder.CreateAsync([text], cancellationToken);

        var end = Py.TimeSeconds();
        var logger = Edges.Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var chars = Py.Len(text);
            var ms = Py.Str((end - start) * 1000);
            Edges.Log.EmbeddedEdge(logger, Uuid, chars, ms);
        }

        return FactEmbedding;
    }

    internal async Task LoadFactEmbeddingAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EdgeLoadEmbeddingsAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)
            RETURN e.fact_embedding AS fact_embedding
""" + "\n        ";

        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)
                RETURN [x IN split(e.fact_embedding, ",") | toFloat(x)] as fact_embedding
""" + "\n            ";
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            query,
            new() { ["uuid"] = Uuid, ["routing_"] = "r" },
            cancellationToken);

        if (records.Count == 0)
        {
            throw new EdgeNotFoundException(Uuid);
        }

        // PORT: Edge has no validate_assignment, so Python stores the record value as it is. The property is a
        // List<double>?, so the value is read as list[float] | None.
        // PORT: the reader converts or rejects a value that Python stores as it is (R7).
        FactEmbedding = Pydantic.Optional(records[0]["fact_embedding"], x => Pydantic.List(x, Pydantic.Float));
    }

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.EdgeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var edgeData = new OrderedDictionary<string, object?>
        {
            ["source_uuid"] = SourceNodeUuid,
            ["target_uuid"] = TargetNodeUuid,
            ["uuid"] = Uuid,
            ["name"] = Name,
            ["group_id"] = GroupId,
            ["fact"] = Fact,
            ["fact_embedding"] = FactEmbedding,
            ["episodes"] = Episodes,
            ["created_at"] = CreatedAt,
            ["expired_at"] = ExpiredAt,
            ["valid_at"] = ValidAt,
            ["invalid_at"] = InvalidAt,
            ["reference_time"] = ReferenceTime,
        };

        // PORT: `self.attributes or {}`. Attributes is never null, and an empty dict gives no item either way.
        foreach (var (k, v) in Attributes)
        {
            if (!edgeData.ContainsKey(k))
            {
                edgeData[k] = v;
            }
        }

        await driver.ExecuteQueryAsync(
            EdgeDbQueries.GetEntityEdgeSaveQuery(driver.Provider),
            new() { ["edge_data"] = edgeData },
            cancellationToken);

        Edges.Log.SavedEdge(Edges.Logger, Uuid);
    }

    /// <summary>
    /// Get an entity edge by UUID.
    ///
    /// Parameters
    /// ----------
    /// driver : GraphDriver
    ///     The graph driver.
    /// uuid : str
    ///     The edge UUID.
    /// group_id : str | None
    ///     If set, the edge must belong to this group or EdgeNotFoundError is raised.
    ///     If None, the lookup does not check the group.
    ///
    /// Raises
    /// ------
    /// EdgeNotFoundError
    ///     If the edge is not found in the selected group.
    /// </summary>
    internal static async Task<EntityEdge> GetByUuidAsync(
        GraphDriver driver,
        string uuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            // PORT: try, except, and else (Part 3): a flag that the code sets after the call returned.
            EntityEdge? edge = null;
            var returned = false;
            try
            {
                edge = await driver.GraphOperationsInterface.EdgeGetByUuidAsync(typeof(EntityEdge), driver, uuid, cancellationToken);
                returned = true;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }

            if (returned)
            {
                if (groupId is not null && Py.NotNone(edge, "group_id").GroupId != groupId)
                {
                    throw new EdgeNotFoundException(uuid);
                }

                return Py.NotNone(edge, "uuid");
            }
        }

        var matchQuery = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)
""" + "\n        ";
        var groupFilter = groupId is not null ? "WHERE e.group_id = $group_id" : string.Empty;
        var (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery
            + groupFilter
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider),
            new() { ["uuid"] = uuid, ["group_id"] = groupId, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();
        if (groupId is not null)
        {
            edges = edges.Where(e => e.GroupId == groupId).ToList();
        }

        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal static async Task<List<EntityEdge>> GetBetweenNodesAsync(
        GraphDriver driver,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EdgeGetBetweenNodesAsync(
                    typeof(EntityEdge),
                    driver,
                    sourceNodeUuid,
                    targetNodeUuid,
                    cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var matchQuery = "\n" + """
            MATCH (n:Entity {uuid: $source_node_uuid})-[e:RELATES_TO]->(m:Entity {uuid: $target_node_uuid})
""" + "\n        ";
        var (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider),
            new() { ["source_node_uuid"] = sourceNodeUuid, ["target_node_uuid"] = targetNodeUuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        return edges;
    }

    /// <summary>
    /// Get entity edges by UUID.
    ///
    /// Parameters
    /// ----------
    /// driver : GraphDriver
    ///     The graph driver.
    /// uuids : list[str]
    ///     The edge UUIDs.
    /// group_id : str | None
    ///     If set, edges from other groups are excluded. If None, no group check is performed.
    /// </summary>
    internal static async Task<List<EntityEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            // PORT: try, except, and else (Part 3): a flag that the code sets after the call returned.
            List<EntityEdge>? found = null;
            var returned = false;
            try
            {
                found = await driver.GraphOperationsInterface.EdgeGetByUuidsAsync(typeof(EntityEdge), driver, uuids, cancellationToken);
                returned = true;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }

            if (returned)
            {
                if (groupId is not null)
                {
                    found = Py.NotNone(found, "__iter__").Where(edge => edge.GroupId == groupId).ToList();
                }

                return Py.NotNone(found, "__iter__");
            }
        }

        if (uuids.Count == 0)
        {
            return [];
        }

        var groupFilter = groupId is not null ? "AND e.group_id = $group_id" : string.Empty;
        var matchQuery = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
            WHERE e.uuid IN $uuids
""" + "\n            "
            + groupFilter
            + "\n" + """
            RETURN
""" + "\n            ";
        var (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider),
            new() { ["uuids"] = uuids, ["group_id"] = groupId, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();
        if (groupId is not null)
        {
            edges = edges.Where(edge => edge.GroupId == groupId).ToList();
        }

        return edges;
    }

    internal static async Task<List<EntityEdge>> GetByGroupIdsAsync(
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
                return await driver.GraphOperationsInterface.EdgeGetByGroupIdsAsync(
                    typeof(EntityEdge),
                    driver,
                    groupIds,
                    limit,
                    uuidCursor,
                    cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var cursorQuery = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitQuery = limit is not null ? "LIMIT $limit" : string.Empty;
        var withEmbeddingsQuery = withEmbeddings
            ? ",\n" + """
                e.fact_embedding AS fact_embedding
""" + "\n                "
            : string.Empty;

        var matchQuery = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
""" + "\n        ";
        var (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery
            + "\n" + """
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider)
            + withEmbeddingsQuery
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitQuery,
            new() { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        if (edges.Count == 0)
        {
            throw new GroupsEdgesNotFoundException(groupIds);
        }

        return edges;
    }

    internal static async Task<List<EntityEdge>> GetByNodeUuidAsync(
        GraphDriver driver,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.EdgeGetByNodeUuidAsync(typeof(EntityEdge), driver, nodeUuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var matchQuery = "\n" + """
            MATCH (n:Entity {uuid: $node_uuid})-[e:RELATES_TO]-(m:Entity)
""" + "\n        ";
        var (records, _, _) = await driver.ExecuteQueryAsync(
            matchQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider),
            new() { ["node_uuid"] = nodeUuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        return edges;
    }
}

internal sealed class CommunityEdge : Edge, IPydanticModel
{
    internal CommunityEdge Validate() => this;

    internal static CommunityEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CommunityEdge", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var groupId = f.Required("group_id", Pydantic.Str);
        var sourceNodeUuid = f.Required("source_node_uuid", Pydantic.Str);
        var targetNodeUuid = f.Required("target_node_uuid", Pydantic.Str);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        f.ThrowIfAny();
        return new CommunityEdge
        {
            Uuid = uuid.Value,
            GroupId = groupId.Value,
            SourceNodeUuid = sourceNodeUuid.Value,
            TargetNodeUuid = targetNodeUuid.Value,
            CreatedAt = createdAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("group_id", GroupId),
        ("source_node_uuid", SourceNodeUuid),
        ("target_node_uuid", TargetNodeUuid),
        ("created_at", CreatedAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CommunityEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    // PORT: model_copy() (R7). MemberwiseClone copies each property and validates nothing.
    internal CommunityEdge ModelCopy() => (CommunityEdge)MemberwiseClone();

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.CommunityEdgeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            EdgeDbQueries.GetCommunityEdgeSaveQuery(driver.Provider),
            new()
            {
                ["community_uuid"] = SourceNodeUuid,
                ["entity_uuid"] = TargetNodeUuid,
                ["uuid"] = Uuid,
                ["group_id"] = GroupId,
                ["created_at"] = CreatedAt,
            },
            cancellationToken);

        Edges.Log.SavedEdge(Edges.Logger, Uuid);
    }

    internal static async Task<CommunityEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.CommunityEdgeGetByUuidAsync(typeof(CommunityEdge), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Community)-[e:HAS_MEMBER {uuid: $uuid}]->(m)
            RETURN
""" + "\n            "
            + EdgeDbQueries.CommunityEdgeReturn,
            new() { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetCommunityEdgeFromRecord).ToList();

        return edges[0];
    }

    internal static async Task<List<CommunityEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.CommunityEdgeGetByUuidsAsync(typeof(CommunityEdge), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Community)-[e:HAS_MEMBER]->(m)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            "
            + EdgeDbQueries.CommunityEdgeReturn,
            new() { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetCommunityEdgeFromRecord).ToList();

        return edges;
    }

    internal static async Task<List<CommunityEdge>> GetByGroupIdsAsync(
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
                return await driver.GraphOperationsInterface.CommunityEdgeGetByGroupIdsAsync(
                    typeof(CommunityEdge),
                    driver,
                    groupIds,
                    limit,
                    uuidCursor,
                    cancellationToken);
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
            MATCH (n:Community)-[e:HAS_MEMBER]->(m)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.CommunityEdgeReturn
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitQuery,
            new() { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetCommunityEdgeFromRecord).ToList();

        return edges;
    }
}

internal sealed class HasEpisodeEdge : Edge, IPydanticModel
{
    internal HasEpisodeEdge Validate() => this;

    internal static HasEpisodeEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("HasEpisodeEdge", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var groupId = f.Required("group_id", Pydantic.Str);
        var sourceNodeUuid = f.Required("source_node_uuid", Pydantic.Str);
        var targetNodeUuid = f.Required("target_node_uuid", Pydantic.Str);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        f.ThrowIfAny();
        return new HasEpisodeEdge
        {
            Uuid = uuid.Value,
            GroupId = groupId.Value,
            SourceNodeUuid = sourceNodeUuid.Value,
            TargetNodeUuid = targetNodeUuid.Value,
            CreatedAt = createdAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("group_id", GroupId),
        ("source_node_uuid", SourceNodeUuid),
        ("target_node_uuid", TargetNodeUuid),
        ("created_at", CreatedAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("HasEpisodeEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    // PORT: model_copy() (R7). MemberwiseClone copies each property and validates nothing.
    internal HasEpisodeEdge ModelCopy() => (HasEpisodeEdge)MemberwiseClone();

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.HasEpisodeEdgeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            EdgeDbQueries.HasEpisodeEdgeSave,
            new()
            {
                ["saga_uuid"] = SourceNodeUuid,
                ["episode_uuid"] = TargetNodeUuid,
                ["uuid"] = Uuid,
                ["group_id"] = GroupId,
                ["created_at"] = CreatedAt,
            },
            cancellationToken);

        Edges.Log.SavedEdge(Edges.Logger, Uuid);
    }

    internal override async Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.HasEpisodeEdgeDeleteAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Saga)-[e:HAS_EPISODE {uuid: $uuid}]->(m:Episodic)
            DELETE e
""" + "\n            ",
            new() { ["uuid"] = Uuid },
            cancellationToken);

        Edges.Log.DeletedEdge(Edges.Logger, Uuid);
    }

    internal static async Task<HasEpisodeEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.HasEpisodeEdgeGetByUuidAsync(typeof(HasEpisodeEdge), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Saga)-[e:HAS_EPISODE {uuid: $uuid}]->(m:Episodic)
            RETURN
""" + "\n            "
            + EdgeDbQueries.HasEpisodeEdgeReturn,
            new() { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetHasEpisodeEdgeFromRecord).ToList();

        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal static async Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.HasEpisodeEdgeGetByUuidsAsync(typeof(HasEpisodeEdge), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Saga)-[e:HAS_EPISODE]->(m:Episodic)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            "
            + EdgeDbQueries.HasEpisodeEdgeReturn,
            new() { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetHasEpisodeEdgeFromRecord).ToList();

        return edges;
    }

    internal static async Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
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
                return await driver.GraphOperationsInterface.HasEpisodeEdgeGetByGroupIdsAsync(
                    typeof(HasEpisodeEdge),
                    driver,
                    groupIds,
                    limit,
                    uuidCursor,
                    cancellationToken);
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
            MATCH (n:Saga)-[e:HAS_EPISODE]->(m:Episodic)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.HasEpisodeEdgeReturn
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitQuery,
            new() { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetHasEpisodeEdgeFromRecord).ToList();

        return edges;
    }
}

internal sealed class NextEpisodeEdge : Edge, IPydanticModel
{
    internal NextEpisodeEdge Validate() => this;

    internal static NextEpisodeEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NextEpisodeEdge", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var groupId = f.Required("group_id", Pydantic.Str);
        var sourceNodeUuid = f.Required("source_node_uuid", Pydantic.Str);
        var targetNodeUuid = f.Required("target_node_uuid", Pydantic.Str);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        f.ThrowIfAny();
        return new NextEpisodeEdge
        {
            Uuid = uuid.Value,
            GroupId = groupId.Value,
            SourceNodeUuid = sourceNodeUuid.Value,
            TargetNodeUuid = targetNodeUuid.Value,
            CreatedAt = createdAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("group_id", GroupId),
        ("source_node_uuid", SourceNodeUuid),
        ("target_node_uuid", TargetNodeUuid),
        ("created_at", CreatedAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("NextEpisodeEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    // PORT: model_copy() (R7). MemberwiseClone copies each property and validates nothing.
    internal NextEpisodeEdge ModelCopy() => (NextEpisodeEdge)MemberwiseClone();

    internal override async Task SaveAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NextEpisodeEdgeSaveAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            EdgeDbQueries.NextEpisodeEdgeSave,
            new()
            {
                ["source_episode_uuid"] = SourceNodeUuid,
                ["target_episode_uuid"] = TargetNodeUuid,
                ["uuid"] = Uuid,
                ["group_id"] = GroupId,
                ["created_at"] = CreatedAt,
            },
            cancellationToken);

        Edges.Log.SavedEdge(Edges.Logger, Uuid);
    }

    internal override async Task DeleteAsync(GraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.NextEpisodeEdgeDeleteAsync(this, driver, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE {uuid: $uuid}]->(m:Episodic)
            DELETE e
""" + "\n            ",
            new() { ["uuid"] = Uuid },
            cancellationToken);

        Edges.Log.DeletedEdge(Edges.Logger, Uuid);
    }

    internal static async Task<NextEpisodeEdge> GetByUuidAsync(GraphDriver driver, string uuid, CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NextEpisodeEdgeGetByUuidAsync(typeof(NextEpisodeEdge), driver, uuid, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE {uuid: $uuid}]->(m:Episodic)
            RETURN
""" + "\n            "
            + EdgeDbQueries.NextEpisodeEdgeReturn,
            new() { ["uuid"] = uuid, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetNextEpisodeEdgeFromRecord).ToList();

        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal static async Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.NextEpisodeEdgeGetByUuidsAsync(typeof(NextEpisodeEdge), driver, uuids, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE]->(m:Episodic)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            "
            + EdgeDbQueries.NextEpisodeEdgeReturn,
            new() { ["uuids"] = uuids, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetNextEpisodeEdgeFromRecord).ToList();

        return edges;
    }

    internal static async Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
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
                return await driver.GraphOperationsInterface.NextEpisodeEdgeGetByGroupIdsAsync(
                    typeof(NextEpisodeEdge),
                    driver,
                    groupIds,
                    limit,
                    uuidCursor,
                    cancellationToken);
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
            MATCH (n:Episodic)-[e:NEXT_EPISODE]->(m:Episodic)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.NextEpisodeEdgeReturn
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitQuery,
            new() { ["group_ids"] = groupIds, ["uuid"] = uuidCursor, ["limit"] = limit, ["routing_"] = "r" },
            cancellationToken);

        var edges = records.Select(Edges.GetNextEpisodeEdgeFromRecord).ToList();

        return edges;
    }
}

internal static partial class Edges
{
    // Edge helpers
    internal static EpisodicEdge GetEpisodicEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        return EpisodicEdge.ModelValidate(new()
        {
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore
        });
    }

    internal static EntityEdge GetEntityEdgeFromRecord(OrderedDictionary<string, object?> record, GraphProvider provider)
    {
        var episodes = record["episodes"];
        var attributes = Py.AsDict(record["attributes"]);
        attributes.Remove("uuid");
        attributes.Remove("source_node_uuid");
        attributes.Remove("target_node_uuid");
        attributes.Remove("fact");
        attributes.Remove("fact_embedding");
        attributes.Remove("name");
        attributes.Remove("group_id");
        attributes.Remove("episodes");
        attributes.Remove("created_at");
        attributes.Remove("expired_at");
        attributes.Remove("valid_at");
        attributes.Remove("invalid_at");
        attributes.Remove("reference_time");

        var edge = EntityEdge.ModelValidate(new()
        {
            ["uuid"] = record["uuid"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["fact"] = record["fact"],
            ["fact_embedding"] = Py.Get(record, "fact_embedding"),
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["episodes"] = episodes,
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore
            ["expired_at"] = Helpers.ParseDbDate(record["expired_at"]),
            ["valid_at"] = Helpers.ParseDbDate(record["valid_at"]),
            ["invalid_at"] = Helpers.ParseDbDate(record["invalid_at"]),
            ["reference_time"] = Helpers.ParseDbDate(Py.Get(record, "reference_time")),
            ["attributes"] = attributes,
        });

        return edge;
    }

    internal static CommunityEdge GetCommunityEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        return CommunityEdge.ModelValidate(new()
        {
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore
        });
    }

    internal static HasEpisodeEdge GetHasEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        return HasEpisodeEdge.ModelValidate(new()
        {
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore
        });
    }

    internal static NextEpisodeEdge GetNextEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        return NextEpisodeEdge.ModelValidate(new()
        {
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore
        });
    }

    internal static async Task CreateEntityEdgeEmbeddingsAsync(
        EmbedderClient embedder,
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        // filter out falsey values from edges
        var filteredEdges = edges.Where(edge => !string.IsNullOrEmpty(edge.Fact)).ToList();

        if (filteredEdges.Count == 0)
        {
            return;
        }

        var factEmbeddings = await embedder.CreateBatchAsync(filteredEdges.Select(edge => edge.Fact).ToList(), cancellationToken);

        // PORT: zip(..., strict=True) (Part 3): each pair is assigned before the error for a count that differs.
        foreach (var (edge, factEmbedding) in Py.ZipStrict(filteredEdges, factEmbeddings))
        {
            edge.FactEmbedding = factEmbedding;
        }
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Edge: {Uuid}")]
        internal static partial void DeletedEdge(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Edges: {Uuids}")]
        internal static partial void DeletedEdges(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved edge to Graph: {Uuid}")]
        internal static partial void SavedEdge(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "embedded edge {Uuid} fact ({Chars} chars) in {Ms} ms")]
        internal static partial void EmbeddedEdge(ILogger logger, string uuid, long chars, string ms);
    }
}
