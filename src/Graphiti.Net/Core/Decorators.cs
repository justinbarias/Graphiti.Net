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
using Graphiti.Core.Search;

// PORT: `F = TypeVar('F', bound=Callable[..., Awaitable[Any]])` has no C# form. The explicit wrapper of R16 takes the
// decorated body as a lambda.
internal static class Decorators
{
    // PORT: the explicit wrapper of R16. The decorated method ports its body to <Name>CoreAsync and calls
    // `Decorators.HandleMultipleGroupIdsAsync(Clients.Driver, MaxCoroutines, groupIds, driver, (ids, d) => ...)`.
    // The lambda receives the group_ids and the driver of each call, which Python rewrites by name. The wrapper reads
    // only `self.clients.driver` and `self.max_coroutines`, so it takes the two values and not `self` (R15, R16): the
    // host of the upstream test is not a Graphiti. A null clientsDriver stands for a `self` with no `clients.driver`.
    // Python merges by the type of the first result. The three overloads are the three result shapes of the four
    // upstream uses (graphiti.py:1546, :2269, :2315, :2391) and of the upstream test: a list, SearchResults, and a
    // tuple of two lists. The branch `else: return results` has no typed form and no caller.
    // TODO(port): handle_multiple_group_ids
    internal static Task<List<TItem>> HandleMultipleGroupIdsAsync<TItem>(
        GraphDriver? clientsDriver,
        long? maxCoroutines,
        List<string>? groupIds,
        GraphDriver? driver,
        Func<List<string>?, GraphDriver?, Task<List<TItem>>> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): handle_multiple_group_ids");
    }

    // TODO(port): handle_multiple_group_ids
    internal static Task<SearchResults> HandleMultipleGroupIdsAsync(
        GraphDriver? clientsDriver,
        long? maxCoroutines,
        List<string>? groupIds,
        GraphDriver? driver,
        Func<List<string>?, GraphDriver?, Task<SearchResults>> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): handle_multiple_group_ids");
    }

    // TODO(port): handle_multiple_group_ids
    internal static Task<(List<T1> First, List<T2> Second)> HandleMultipleGroupIdsAsync<T1, T2>(
        GraphDriver? clientsDriver,
        long? maxCoroutines,
        List<string>? groupIds,
        GraphDriver? driver,
        Func<List<string>?, GraphDriver?, Task<(List<T1> First, List<T2> Second)>> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): handle_multiple_group_ids");
    }

    // PORT: the three overloads share the branches of the Python wrapper in this one method, which takes the merge
    // of the result shape (R16 allows this one helper).
    // TODO(port): handle_multiple_group_ids
    private static Task<T> HandleMultipleGroupIdsCoreAsync<T>(
        GraphDriver? clientsDriver,
        long? maxCoroutines,
        List<string>? groupIds,
        GraphDriver? driver,
        Func<List<string>?, GraphDriver?, Task<T>> func,
        Func<List<T>, T> merge,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("TODO(port): handle_multiple_group_ids");
    }

    // PORT: get_parameter_position reads the signature of a function with inspect.signature. The explicit wrapper
    // receives group_ids and driver as parameters, so it needs no position (R16). Its one caller is the wrapper above,
    // and no judge case calls it.
}
