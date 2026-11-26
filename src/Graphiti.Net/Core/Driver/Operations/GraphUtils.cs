// Ported from graphiti_core/driver/operations/graph_utils.py
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

namespace Graphiti.Core.Driver.Operations;

using Graphiti.PyCompat;

internal sealed record Neighbor : IPydanticModel
{
    internal required string NodeUuid { get; set; }

    internal required long EdgeCount { get; set; }

    internal Neighbor Validate() => this;

    internal static Neighbor ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Neighbor", data);
        var nodeUuid = f.Required("node_uuid", Pydantic.Str);
        var edgeCount = f.Required("edge_count", Pydantic.Int);
        f.ThrowIfAny();
        return new Neighbor { NodeUuid = nodeUuid.Value, EdgeCount = edgeCount.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("node_uuid", NodeUuid), ("edge_count", EdgeCount));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("Neighbor")
        .Required("node_uuid", PydanticType.Str)
        .Required("edge_count", PydanticType.Int);
}

internal static class GraphUtils
{
    // PORT: upstream defect U11 (R21, GAPS F6). label_propagation does not end for some inputs. Port it as it is.
    internal static List<List<string>> LabelPropagation(OrderedDictionary<string, List<Neighbor>> projection)
    {
        // PORT: enumerate(projection.keys()) is a loop with a counter. Python ints are long (R6).
        var communityMap = new OrderedDictionary<string, long>();
        long index = 0;
        foreach (var uuid in projection.Keys)
        {
            communityMap[uuid] = index;
            index++;
        }

        while (true)
        {
            var noChange = true;
            var newCommunityMap = new OrderedDictionary<string, long>();

            foreach (var (uuid, neighbors) in projection)
            {
                var currCommunity = communityMap[uuid];

                // PORT: defaultdict(int) with d[k] += n (R14).
                var communityCandidates = new OrderedDictionary<long, long>();
                foreach (var neighbor in neighbors)
                {
                    var key = communityMap[neighbor.NodeUuid];
                    communityCandidates[key] = Py.Get(communityCandidates, key, 0L) + neighbor.EdgeCount;
                }

                // PORT: sort(reverse=True) on (count, community) tuples of ints is OrderByDescending and ThenByDescending (R14).
                var communityLst = communityCandidates
                    .Select(item => (Count: item.Value, Community: item.Key))
                    .OrderByDescending(item => item.Count)
                    .ThenByDescending(item => item.Community)
                    .ToList();

                var (candidateRank, communityCandidate) = communityLst.Count > 0 ? communityLst[0] : (0L, -1L);
                long newCommunity;
                if (communityCandidate != -1 && candidateRank > 1)
                {
                    newCommunity = communityCandidate;
                }
                else
                {
                    newCommunity = Math.Max(communityCandidate, currCommunity);
                }

                newCommunityMap[uuid] = newCommunity;

                if (newCommunity != currCommunity)
                {
                    noChange = false;
                }
            }

            if (noChange)
            {
                break;
            }

            communityMap = newCommunityMap;
        }

        var communityClusterMap = new OrderedDictionary<long, List<string>>();
        foreach (var (uuid, community) in communityMap)
        {
            Py.SetDefault(communityClusterMap, community, new List<string>()).Add(uuid);
        }

        return [.. communityClusterMap.Values];
    }
}
