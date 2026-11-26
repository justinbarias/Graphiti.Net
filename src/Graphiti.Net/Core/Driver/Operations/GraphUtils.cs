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

// PORT: no module of packet S1 calls a model member of Neighbor, so it has its fields only (R7).
internal sealed record Neighbor
{
    internal required string NodeUuid { get; set; }

    internal required long EdgeCount { get; set; }
}

internal static class GraphUtils
{
    // PORT: upstream defect U11 (R21, GAPS F6). label_propagation does not end for some inputs. Port it as it is.
    // TODO(port): label_propagation
    internal static List<List<string>> LabelPropagation(OrderedDictionary<string, List<Neighbor>> projection)
    {
        throw new NotSupportedException("TODO(port): label_propagation");
    }
}
