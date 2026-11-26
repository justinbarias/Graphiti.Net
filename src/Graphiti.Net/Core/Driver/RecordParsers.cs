// Ported from graphiti_core/driver/record_parsers.py
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

namespace Graphiti.Core.Driver;

using Graphiti.PyCompat;

internal static class RecordParsers
{
    /// <summary>Parse an entity node from a database record.</summary>
    internal static EntityNode EntityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: attributes.pop(...) on a value from open data that can be None raises AttributeError (R6, Py.NotNone).
        var attributes = Py.AsDict(Py.NotNone(record["attributes"], "pop"));
        attributes.Remove("uuid");
        attributes.Remove("name");
        attributes.Remove("group_id");
        attributes.Remove("name_embedding");
        attributes.Remove("summary");
        attributes.Remove("created_at");
        attributes.Remove("labels");

        // PORT: labels is read as open data here, and `in` and remove are the first calls on it. The typed read is at that site (R6).
        var labels = Py.Get(record, "labels", (object?)new List<object?>());
        var groupId = Py.Get(record, "group_id");
        var dynamicLabel = "Entity_" + Py.AsString(Py.NotNone(groupId, "replace")).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Py.Contains(Py.AsList(labels), dynamicLabel))
        {
            Py.Remove(Py.AsList(labels), dynamicLabel);
        }

        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return EntityNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["name_embedding"] = Py.Get(record, "name_embedding"),
            ["group_id"] = groupId,
            ["labels"] = labels,
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore[arg-type]
            ["summary"] = record["summary"],
            ["attributes"] = attributes,
        });
    }

    /// <summary>Parse an entity edge from a database record.</summary>
    internal static EntityEdge EntityEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: attributes.pop(...) on a value from open data that can be None raises AttributeError (R6, Py.NotNone).
        var attributes = Py.AsDict(Py.NotNone(record["attributes"], "pop"));
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

        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return EntityEdge.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["fact"] = record["fact"],
            ["fact_embedding"] = Py.Get(record, "fact_embedding"),
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["episodes"] = record["episodes"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore[arg-type]
            ["expired_at"] = Helpers.ParseDbDate(record["expired_at"]),
            ["valid_at"] = Helpers.ParseDbDate(record["valid_at"]),
            ["invalid_at"] = Helpers.ParseDbDate(record["invalid_at"]),
            ["reference_time"] = Helpers.ParseDbDate(Py.Get(record, "reference_time")),
            ["attributes"] = attributes,
        });
    }

    /// <summary>Parse an episodic node from a database record.</summary>
    internal static EpisodicNode EpisodicNodeFromRecord(OrderedDictionary<string, object?> record)
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

    /// <summary>Parse a community node from a database record.</summary>
    internal static CommunityNode CommunityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return CommunityNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["name_embedding"] = record["name_embedding"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore[arg-type]
            ["summary"] = record["summary"],
        });
    }
}
