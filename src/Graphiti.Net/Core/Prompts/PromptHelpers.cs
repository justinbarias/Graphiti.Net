// Ported from graphiti_core/prompts/prompt_helpers.py
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

namespace Graphiti.Core.Prompts;

using Graphiti.PyCompat;

/// <summary>The module-level members of <c>prompts/prompt_helpers.py</c>.</summary>
internal static class PromptHelpers
{
    internal const string DoNotEscapeUnicode = "\nDo not escape unicode characters.\n";

    /// <summary>
    /// Serialize data to JSON for use in prompts.
    ///
    /// Args:
    ///     data: The data to serialize
    ///     ensure_ascii: If True, escape non-ASCII characters. If False (default), preserve them.
    ///     indent: Number of spaces for indentation. Defaults to None (minified).
    ///
    /// Returns:
    ///     JSON string representation of the data
    ///
    /// Notes:
    ///     By default (ensure_ascii=False), non-ASCII characters (e.g., Korean, Japanese, Chinese)
    ///     are preserved in their original form in the prompt, making them readable
    ///     in LLM logs and improving model understanding.
    /// </summary>
    /// <param name="data">The data to serialize.</param>
    /// <param name="ensureAscii">If true, escape non-ASCII characters.</param>
    /// <param name="indent">Number of spaces for indentation, or null for minified output.</param>
    /// <returns>The JSON string.</returns>
    internal static string ToPromptJson(object? data, bool ensureAscii = false, long? indent = null)
    {
        return PyJson.Dumps(data, ensureAscii, indent);
    }
}
