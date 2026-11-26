// Ported from graphiti_core/search/search.py
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

namespace Graphiti.Core.Search;

using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// PORT: the module name equals its folder name, so the module class has the suffix Module (R2).
internal static class SearchModule
{
    private static ILogger Logger => PortServices.Logger(typeof(SearchModule));

    // PORT: `value.value if hasattr(value, 'value') else value` is two overloads (R15): one for text, one for an enum
    // that returns its wire value.
    // TODO(port): _enum_value
    private static string EnumValue(string value)
    {
        throw new NotSupportedException("TODO(port): _enum_value");
    }

    // TODO(port): _enum_value
    private static object EnumValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        throw new NotSupportedException("TODO(port): _enum_value");
    }

    // TODO(port): _resolve_tracer
    private static Tracer ResolveTracer(Tracer? searchTracer)
    {
        throw new NotSupportedException("TODO(port): _resolve_tracer");
    }

    // PORT: a @contextmanager that sets the span status from the exception of its body. It takes the body as a lambda,
    // as Tracer.StartSpanAsync does (R16, decision D45): `with _trace_phase(t, name, attrs) as span: ...` is
    // `await TracePhaseAsync(t, name, attrs, async span => { ... }, cancellationToken)`, and a `return` in the body is
    // the result of the call. `attributes` has no default, because the body follows it. Each upstream call passes it.
    // TODO(port): _trace_phase
    private static Task<T> TracePhaseAsync<T>(
        Tracer searchTracer,
        string name,
        OrderedDictionary<string, object?>? attributes,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _trace_phase");
    }

    // PORT: the form for a body with no result, as Tracer.StartSpanAsync has. Python has one _trace_phase for both.
    // TODO(port): _trace_phase
    private static Task TracePhaseAsync(
        Tracer searchTracer,
        string name,
        OrderedDictionary<string, object?>? attributes,
        Func<TracerSpan, Task> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _trace_phase");
    }

    // PORT: the four searches run through the four-lambda form of Helpers.SemaphoreGatherAsync (R8).
    // TODO(port): search
    internal static Task<SearchResults> SearchAsync(
        GraphitiClients clients,
        string query,
        List<string>? groupIds,
        SearchConfig config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        List<double>? queryVector = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): edge_search
    internal static Task<(List<EntityEdge>, List<double>)> EdgeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        EdgeSearchConfig? config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): edge_search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): node_search
    internal static Task<(List<EntityNode>, List<double>)> NodeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        NodeSearchConfig? config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): node_search");
    }

    // PORT: the parameter `_query_vector` is `queryVector` (R2). `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): episode_search
    internal static Task<(List<EpisodicNode>, List<double>)> EpisodeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        EpisodeSearchConfig? config,
        SearchFilters searchFilter,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): episode_search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): community_search
    internal static Task<(List<CommunityNode>, List<double>)> CommunitySearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        CommunitySearchConfig? config,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): community_search");
    }
}
