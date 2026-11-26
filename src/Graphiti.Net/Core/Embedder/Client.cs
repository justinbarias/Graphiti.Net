// Ported from graphiti_core/embedder/client.py
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

namespace Graphiti.Core.Embedder;

using Graphiti.PyCompat;

internal static class Client
{
    internal static readonly long EmbeddingDim = PyEnv.Int("EMBEDDING_DIM", 1024);
}

// PORT: the field is frozen, so it has an init accessor (R7). No module of packet S1 calls a model member.
internal sealed record EmbedderConfig
{
    internal long EmbeddingDim { get; init; } = Client.EmbeddingDim;
}

// PORT: an abstract method has no body, as in Python.
internal abstract class EmbedderClient
{
    // TODO(port): no rule for the union `str | list[str] | Iterable[int] | Iterable[Iterable[int]]` of input_data.
    // It is object, which holds a value of the value model (R6). Each upstream caller passes a list[str].
    internal abstract Task<List<double>> CreateAsync(
        object inputData,
        CancellationToken cancellationToken = default);

    internal virtual Task<List<List<double>>> CreateBatchAsync(
        List<string> inputDataList,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }
}
