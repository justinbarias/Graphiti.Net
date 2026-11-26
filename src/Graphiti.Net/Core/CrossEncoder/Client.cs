// Ported from graphiti_core/cross_encoder/client.py
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

namespace Graphiti.Core.CrossEncoder;

// PORT: an abstract method has no body, as in Python.
internal abstract class CrossEncoderClient
{
    internal abstract Task<List<(string, double)>> RankAsync(
        string query,
        List<string> passages,
        CancellationToken cancellationToken = default);
}
