// Ported from graphiti_core/utils/datetime_utils.py
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

namespace Graphiti.Core.Utils;

using Graphiti.Internal;
using Graphiti.PyCompat;

// PORT: the pilot unit entity_node_ops ports only utc_now of this module (RULES.md R24).
internal static class DatetimeUtils
{
    /// <summary>Returns the current UTC datetime with timezone information.</summary>
    internal static PyDateTime UtcNow() => PyDateTime.UtcNow(PortServices.Time);
}
