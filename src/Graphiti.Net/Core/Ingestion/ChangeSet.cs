// Ported from graphiti_core/ingestion/change_set.py
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

namespace Graphiti.Core.Ingestion;

// PORT: a shell of the dataclass ResolutionTrace (R7), for the `trace` parameters of utils.maintenance and
// utils.bulk_utils. The packet of ingestion/change_set.py fills it.
internal sealed record ResolutionTrace;
