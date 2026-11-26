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

// PORT: shells that packet S1 declares (plan 0004, layer 0), with the name and the base class only. Packet S2 fills
// them. Edge defines __eq__ and __hash__, so it and its subclasses are classes, not records (R7).
internal abstract class Edge;

internal sealed class EpisodicEdge : Edge;

internal sealed class EntityEdge : Edge;

internal sealed class CommunityEdge : Edge;

internal sealed class HasEpisodeEdge : Edge;

internal sealed class NextEpisodeEdge : Edge;
