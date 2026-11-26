// Ported from graphiti_core/helpers.py
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

using System.Text.RegularExpressions;

// PORT: the pilot unit search.search_filters ports only SAFE_CYPHER_IDENTIFIER_PATTERN and validate_node_labels of
// this module (RULES.md R24).
internal static class Helpers
{
    // PORT: internal, not private (R13), because nodes.py and driver/relate/operations/graph_ops.py import it.
    // Python: re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
    internal static readonly Regex SafeCypherIdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>Validate that node labels are safe to interpolate into Cypher label expressions.</summary>
    internal static bool ValidateNodeLabels(List<string>? nodeLabels)
    {
        if (nodeLabels is null || nodeLabels.Count == 0)
        {
            return true;
        }

        var invalidLabels = nodeLabels.Where(label => !SafeCypherIdentifierPattern.IsMatch(label)).ToList();
        if (invalidLabels.Count > 0)
        {
            throw new NodeLabelValidationException(invalidLabels);
        }

        return true;
    }
}
