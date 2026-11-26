// Ported from graphiti_core/models/nodes/node_db_queries.py
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

namespace Graphiti.Core.Models.Nodes;

using Graphiti.Core.Driver;

// PORT: the pilot unit driver.neo4j.operations.entity_node_ops declares only the names that it uses (RULES.md R24).
internal static class NodeDbQueries
{
    // TODO(port): get_entity_node_save_query
    internal static string GetEntityNodeSaveQuery(GraphProvider provider, string labels, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_save_query");
    }

    // TODO(port): get_entity_node_save_bulk_query
    // PORT: the Python return type is `str | Any`, so it is object? (R6). The Neo4j branch returns a string.
    internal static object? GetEntityNodeSaveBulkQuery(GraphProvider provider, List<OrderedDictionary<string, object?>> nodes, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_save_bulk_query");
    }

    // TODO(port): get_entity_node_return_query
    internal static string GetEntityNodeReturnQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_return_query");
    }
}
