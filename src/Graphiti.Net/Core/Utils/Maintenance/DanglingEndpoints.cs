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
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class DanglingEndpoints
{
    private static ILogger Logger => PortServices.Logger(typeof(DanglingEndpoints));

    // PORT: the arguments after `name` are keyword-only in Python. `name_to_node` is read and written by key, and the
    // caller iterates neither the dict nor its order, so it is a Dictionary (R14).

    /// <summary>
    /// Create an EntityNode for an edge endpoint missing from the provided nodes list.
    ///
    /// The LLM sometimes emits edges whose source/target is not also listed in
    /// extracted entities (especially value-like endpoints such as version strings).
    /// Materializing the missing endpoint keeps the fact instead of dropping it.
    /// </summary>
    internal static EntityNode MaterializeDanglingEndpointNode(
        string name,
        string groupId,
        Dictionary<string, EntityNode> nameToNode,
        List<EntityNode> nodes)
    {
        var normalized = DedupHelpers.NormalizeStringExact(name);
        if (nameToNode.TryGetValue(normalized, out var existing) && existing is not null)
        {
            return existing;
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var nameRepr = Py.Repr(name);
            Log.MaterializingDanglingEndpoint(Logger, nameRepr, groupId);
        }

        var newNode = new EntityNode
        {
            Name = PyText.Strip(name),
            GroupId = groupId,
            Labels = ["Entity"],
            Summary = string.Empty,
            CreatedAt = DatetimeUtils.UtcNow(),
        }.Validate();
        nodes.Add(newNode);
        nameToNode[normalized] = newNode;
        return newNode;
    }

    // PORT: the arguments after `nodes` are keyword-only in Python. Both sets are only tested with `in` (R14).
    // `nodes[:] = survivors` changes the list of the caller in place.

    /// <summary>Drop materialized nodes that no kept edge references; return survivors.</summary>
    internal static List<EntityNode> PruneUnreferencedMaterializedNodes(
        List<EntityNode> nodes,
        HashSet<string> initialUuids,
        HashSet<string> referencedUuids)
    {
        var survivors = nodes.Where(node => initialUuids.Contains(node.Uuid) || referencedUuids.Contains(node.Uuid)).ToList();
        var materialized = survivors.Where(node => !initialUuids.Contains(node.Uuid)).ToList();
        if (survivors.Count != nodes.Count)
        {
            nodes.Clear();
            nodes.AddRange(survivors);
        }

        return materialized;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Materializing dangling edge endpoint node for name {Name} (group_id={GroupId})")]
        internal static partial void MaterializingDanglingEndpoint(ILogger logger, string name, string groupId);
    }
}
