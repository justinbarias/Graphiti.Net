// Ported from graphiti_core/utils/maintenance/graph_data_operations.py
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

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Core.Driver;
using Graphiti.Core.Models.Nodes;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static class GraphDataOperations
{
    internal const long EpisodeWindowLen = 3;

    private static ILogger Logger => PortServices.Logger(typeof(GraphDataOperations));

    // PORT: no return annotation. The function returns the result of GraphOperationsInterface.clear_data, which is a
    // Task with no result (TYPE_HINTS.md, R20). The nested work functions delete_all and delete_group_ids are local
    // functions that the unit passes to ExecuteWriteAsync (R9).
    internal static async Task ClearDataAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                await driver.GraphOperationsInterface.ClearDataAsync(driver, groupIds, cancellationToken);
                return;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        await using (var session = driver.Session())
        {
            async Task DeleteAllAsync(Transaction tx)
            {
                await tx.RunAsync("MATCH (n) DETACH DELETE n", new(), cancellationToken);
            }

            async Task DeleteGroupIdsAsync(Transaction tx)
            {
                var labels = new List<string> { "Entity", "Episodic", "Community", "Saga" };

                foreach (var label in labels)
                {
                    await tx.RunAsync(
                        "\n" + $$"""
                    MATCH (n:{{label}})
                    WHERE n.group_id IN $group_ids
                    DETACH DELETE n
""" + "\n                    ",
                        new() { ["group_ids"] = groupIds },
                        cancellationToken);
                }
            }

            if (groupIds is null)
            {
                await session.ExecuteWriteAsync(DeleteAllAsync, cancellationToken);
            }
            else
            {
                await session.ExecuteWriteAsync(DeleteGroupIdsAsync, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Retrieve the last n episodic nodes from the graph.
    ///
    /// Args:
    ///     driver (Driver): The Neo4j driver instance.
    ///     reference_time (datetime): The reference time to filter episodes. Only episodes with a valid_at timestamp
    ///                                less than or equal to this reference_time will be retrieved. This allows for
    ///                                querying the graph's state at a specific point in time.
    ///     last_n (int, optional): The number of most recent episodes to retrieve, relative to the reference_time.
    ///     group_ids (list[str], optional): The list of group ids to return data from.
    ///     source (EpisodeType, optional): Filter episodes by source type.
    ///     saga (str, optional): If provided, only retrieve episodes that belong to the saga with this name.
    ///
    /// Returns:
    ///     list[EpisodicNode]: A list of EpisodicNode objects representing the retrieved episodes.
    /// </summary>
    internal static async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        GraphDriver driver,
        PyDateTime referenceTime,
        long lastN = EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.RetrieveEpisodesAsync(
                    driver, referenceTime, lastN, groupIds, source, saga, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // If saga is provided, retrieve episodes from that saga only
        if (saga is not null)
        {
            var groupId = groupIds is { Count: > 0 } ? groupIds[0] : null;
            var sourceFilter = source is not null ? "AND e.source = $source" : string.Empty;

            var (records, _, _) = await driver.ExecuteQueryAsync(
                "\n" + $$"""
            MATCH (s:Saga {name: $saga_name, group_id: $group_id})-[:HAS_EPISODE]->(e:Episodic)
            WHERE e.valid_at <= $reference_time
            {{sourceFilter}}
            RETURN
""" + "\n            "
                + (driver.Provider == GraphProvider.Neptune
                    ? NodeDbQueries.EpisodicNodeReturnNeptune
                    : NodeDbQueries.EpisodicNodeReturn)
                + "\n" + """
            ORDER BY e.valid_at DESC, e.created_at DESC
            LIMIT $num_episodes
""" + "\n            ",
                new()
                {
                    ["saga_name"] = saga,
                    ["group_id"] = groupId,
                    ["reference_time"] = referenceTime,
                    ["source"] = source is { } sagaSource ? PyEnum.Name(sagaSource) : null,
                    ["num_episodes"] = lastN,
                },
                cancellationToken);

            var sagaEpisodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();
            sagaEpisodes.Reverse();
            return sagaEpisodes; // Return in chronological order
        }

        var queryParams = new OrderedDictionary<string, object?>();
        var queryFilter = string.Empty;
        if (groupIds is { Count: > 0 })
        {
            queryFilter += "\nAND e.group_id IN $group_ids";
            queryParams["group_ids"] = groupIds;
        }

        if (source is { } episodeSource)
        {
            queryFilter += "\nAND e.source = $source";
            queryParams["source"] = PyEnum.Name(episodeSource);
        }

        var query =
            "\n" + """
                                    MATCH (e:Episodic)
                                    WHERE e.valid_at <= $reference_time
""" + "\n                                    "
            + queryFilter
            + "\n" + """
        RETURN
""" + "\n        "
            + (driver.Provider == GraphProvider.Neptune
                ? NodeDbQueries.EpisodicNodeReturnNeptune
                : NodeDbQueries.EpisodicNodeReturn)
            + "\n" + """
        ORDER BY e.valid_at DESC, e.created_at DESC
        LIMIT $num_episodes
""" + "\n        ";

        // PORT: named arguments with a spread: the named keys first, then Add for each key of query_params (R6).
        var parameters = new OrderedDictionary<string, object?>
        {
            ["reference_time"] = referenceTime,
            ["num_episodes"] = lastN,
        };
        foreach (var (key, value) in queryParams)
        {
            parameters.Add(key, value);
        }

        var (result, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);

        var episodes = result.Select(Nodes.GetEpisodicNodeFromRecord).ToList();
        episodes.Reverse();
        return episodes; // Return in chronological order
    }
}
