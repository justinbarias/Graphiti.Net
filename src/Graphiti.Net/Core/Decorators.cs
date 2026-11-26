// Ported from graphiti_core/decorators.py
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

using Graphiti.Core.Driver;

// PORT: `F = TypeVar('F', bound=Callable[..., Awaitable[Any]])` has no C# form. The explicit wrapper of R16 takes the
// decorated body as a lambda.
internal static class Decorators
{
    // PORT: the explicit wrapper of R16. The decorated method ports its body to <Name>CoreAsync and calls
    // `Decorators.HandleMultipleGroupIdsAsync(this, groupIds, driver, (ids, d) => <Name>CoreAsync(...))`. The lambda
    // receives the group_ids and the driver of each call, which Python rewrites by name. `self` is the Graphiti
    // instance of the four upstream uses (graphiti.py:1546, :2269, :2315, :2391), read with typed access (R15).
    // T is the result of the decorated method: List<EpisodicNode>, (List<CommunityNode>, List<CommunityEdge>),
    // List<EntityEdge>, or SearchResults. The merge of the FalkorDB branch tests the type of the first result.
    // TODO(port): handle_multiple_group_ids
    internal static Task<T> HandleMultipleGroupIdsAsync<T>(
        GraphitiCore self,
        List<string>? groupIds,
        GraphDriver? driver,
        Func<List<string>?, GraphDriver?, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): handle_multiple_group_ids");
    }

    // PORT: get_parameter_position reads the signature of a function with inspect.signature. The explicit wrapper
    // receives group_ids and driver as parameters, so it needs no position (R16: `functools.wraps`, `inspect.signature`
    // are not needed). Its one caller is the wrapper above, and no judge case calls it.
}
