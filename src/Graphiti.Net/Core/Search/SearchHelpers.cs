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

using Graphiti.Core.Prompts;
using Graphiti.PyCompat;

internal static class SearchHelpers
{
    internal static string FormatEdgeDateRange(EntityEdge edge)
    {
        // return f"{datetime(edge.valid_at).strftime('%Y-%m-%d %H:%M:%S') if edge.valid_at else 'date unknown'} - {(edge.invalid_at.strftime('%Y-%m-%d %H:%M:%S') if edge.invalid_at else 'present')}"
        // PORT: a datetime is always true in Python, so the test is a test for None. `{dt}` is `str(dt)` (R11).
        return (edge.ValidAt is { } validAt ? validAt.Str() : "date unknown") + " - " + (edge.InvalidAt is { } invalidAt ? invalidAt.Str() : "present");
    }

    /// <summary>Reformats a set of SearchResults into a single string to pass directly to an LLM as context</summary>
    internal static string SearchResultsToContextString(SearchResults searchResults)
    {
        var factJson = searchResults.Edges
            .Select(edge => (object?)new OrderedDictionary<string, object?>
            {
                ["fact"] = edge.Fact,
                ["valid_at"] = Py.Str(edge.ValidAt),
                // PORT: `edge.invalid_at or 'Present'`. A datetime is always true in Python.
                ["invalid_at"] = Py.Str(edge.InvalidAt is { } invalidAt ? (object)invalidAt : "Present"),
            })
            .ToList();
        var entityJson = searchResults.Nodes
            .Select(node => (object?)new OrderedDictionary<string, object?> { ["entity_name"] = node.Name, ["summary"] = node.Summary })
            .ToList();
        var episodeJson = searchResults.Episodes
            .Select(episode => (object?)new OrderedDictionary<string, object?>
            {
                ["source_description"] = episode.SourceDescription,
                ["content"] = episode.Content,
            })
            .ToList();
        var communityJson = searchResults.Communities
            .Select(community => (object?)new OrderedDictionary<string, object?>
            {
                ["community_name"] = community.Name,
                ["summary"] = community.Summary,
            })
            .ToList();

        var contextString = "\n" + $$"""
    FACTS and ENTITIES represent relevant context to the current conversation.
    COMMUNITIES represent a cluster of closely related entities.

    These are the most relevant facts and their valid and invalid dates. Facts are considered valid
    between their valid_at and invalid_at dates. Facts with an invalid_at date of "Present" are considered valid.
    <FACTS>
            {{PromptHelpers.ToPromptJson(factJson)}}
    </FACTS>
    <ENTITIES>
            {{PromptHelpers.ToPromptJson(entityJson)}}
    </ENTITIES>
    <EPISODES>
            {{PromptHelpers.ToPromptJson(episodeJson)}}
    </EPISODES>
    <COMMUNITIES>
            {{PromptHelpers.ToPromptJson(communityJson)}}
    </COMMUNITIES>
""" + "\n";

        return contextString;
    }
}
