// Ported from graphiti_core/utils/maintenance/dangling_endpoints.py
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

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Internal;
using Microsoft.Extensions.Logging;

internal static class DanglingEndpoints
{
    private static ILogger Logger => PortServices.Logger(typeof(DanglingEndpoints));

    // PORT: the arguments after `name` are keyword-only in Python. `name_to_node` is read and written by key, and the
    // caller iterates neither the dict nor its order, so it is a Dictionary (R14).
    // TODO(port): materialize_dangling_endpoint_node
    internal static EntityNode MaterializeDanglingEndpointNode(
        string name,
        string groupId,
        Dictionary<string, EntityNode> nameToNode,
        List<EntityNode> nodes)
    {
        throw new NotSupportedException("TODO(port): materialize_dangling_endpoint_node");
    }

    // PORT: the arguments after `nodes` are keyword-only in Python. Both sets are only tested with `in` (R14).
    // `nodes[:] = survivors` changes the list of the caller in place.
    // TODO(port): prune_unreferenced_materialized_nodes
    internal static List<EntityNode> PruneUnreferencedMaterializedNodes(
        List<EntityNode> nodes,
        HashSet<string> initialUuids,
        HashSet<string> referencedUuids)
    {
        throw new NotSupportedException("TODO(port): prune_unreferenced_materialized_nodes");
    }
}
