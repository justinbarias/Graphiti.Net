// Ported from graphiti_core/llm_client/cache.py
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

namespace Graphiti.Core.LlmClient;

/// <summary>
/// Simple SQLite + JSON cache for LLM responses.
///
/// Replaces diskcache to avoid unsafe pickle deserialization (CVE in diskcache &lt;= 5.6.3).
/// Only stores JSON-serializable data.
/// </summary>
// PORT: the private attribute `_conn` is not declared. Port/RULES.md has no rule for the SQLite cache, and no project
// references a SQLite package (port/DECISIONS.md lists Microsoft.Data.Sqlite 11.0.0, which the lead has not added).
// The unit stays a skeleton, and each member keeps the marker below.
internal sealed class LLMCache
{
    // TODO(port): no rule for the SQLite cache
    internal LLMCache(string directory)
    {
        throw new NotSupportedException("TODO(port): no rule for the SQLite cache");
    }

    // TODO(port): no rule for the SQLite cache
    internal OrderedDictionary<string, object?>? Get(string key)
    {
        throw new NotSupportedException("TODO(port): no rule for the SQLite cache");
    }

    // TODO(port): no rule for the SQLite cache
    internal void Set(string key, OrderedDictionary<string, object?> value)
    {
        throw new NotSupportedException("TODO(port): no rule for the SQLite cache");
    }

    // TODO(port): no rule for the SQLite cache
    internal void Close()
    {
        throw new NotSupportedException("TODO(port): no rule for the SQLite cache");
    }

    // PORT: __del__ is not ported (R16).
}
