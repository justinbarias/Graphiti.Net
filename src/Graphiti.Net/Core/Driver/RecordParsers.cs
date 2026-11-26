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

internal static class RecordParsers
{
    /// <summary>Parse an entity node from a database record.</summary>
    // TODO(port): entity_node_from_record
    internal static EntityNode EntityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): entity_node_from_record");
    }

    // TODO(port): entity_edge_from_record
    internal static EntityEdge EntityEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): entity_edge_from_record");
    }

    // TODO(port): episodic_node_from_record
    internal static EpisodicNode EpisodicNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): episodic_node_from_record");
    }

    // TODO(port): community_node_from_record
    internal static CommunityNode CommunityNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): community_node_from_record");
    }
}
