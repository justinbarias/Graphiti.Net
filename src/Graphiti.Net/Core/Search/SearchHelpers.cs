// Ported from graphiti_core/search/search_helpers.py
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

internal static class SearchHelpers
{
    // TODO(port): format_edge_date_range
    internal static string FormatEdgeDateRange(EntityEdge edge)
    {
        // return f"{datetime(edge.valid_at).strftime('%Y-%m-%d %H:%M:%S') if edge.valid_at else 'date unknown'} - {(edge.invalid_at.strftime('%Y-%m-%d %H:%M:%S') if edge.invalid_at else 'present')}"
        throw new NotSupportedException("TODO(port): format_edge_date_range");
    }

    // TODO(port): search_results_to_context_string
    internal static string SearchResultsToContextString(SearchResults searchResults)
    {
        throw new NotSupportedException("TODO(port): search_results_to_context_string");
    }
}
