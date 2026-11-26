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

// PORT: the private attribute `_conn` (a Microsoft.Data.Sqlite connection) is not declared. The unit adds it with
// the constructor body.
internal sealed class LLMCache
{
    // TODO(port): LLMCache.__init__
    internal LLMCache(string directory)
    {
        throw new NotSupportedException("TODO(port): LLMCache.__init__");
    }

    // TODO(port): LLMCache.get
    internal OrderedDictionary<string, object?>? Get(string key)
    {
        throw new NotSupportedException("TODO(port): LLMCache.get");
    }

    // TODO(port): LLMCache.set
    internal void Set(string key, OrderedDictionary<string, object?> value)
    {
        throw new NotSupportedException("TODO(port): LLMCache.set");
    }

    // TODO(port): LLMCache.close
    internal void Close()
    {
        throw new NotSupportedException("TODO(port): LLMCache.close");
    }

    // TODO(port): no rule for __del__, which closes the connection and suppresses each exception. A finalizer or
    // IDisposable are the two forms.
}
