// Evaluation test for graph_queries.py, models/edges/edge_db_queries.py, and models/nodes/node_db_queries.py
// (port/RULES.md, R23 and R25). values/query_builders.json holds calls that port/tools/record_query_builders.py
// recorded from Python: each function, each provider, each flag, and labels that are safe, unsafe, empty, not ASCII, and of
// other types of the value model (tuple, bytes, dicts, scalars). It also holds each module constant by name ("const:NAME").
namespace Graphiti.Port.Evaluation;

using System.Collections;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.PyCompat;

public class QueryBuildersValuesTests
{
    [Fact]
    public void The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "query_builders.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.True(cases.Count >= 2500);

        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var name = (string)recorded["f"]!;
            var recordedArgs = recorded["a"];
            var args = (List<object?>)Decode(recordedArgs)!;
            var result = (OrderedDictionary<string, object?>)recorded["r"]!;
            var label = $"{name}({PyJson.Dumps(recordedArgs)})";
            try
            {
                if (result.TryGetValue("err", out var err))
                {
                    var error = (List<object?>)err!;
                    var thrown = Assert.ThrowsAny<Exception>(() => Invoke(name, args));
                    Assert.Equal((string)error[0]!, PythonName(thrown));

                    // A TypeError and a KeyError are compared by type only: the text of .NET differs.
                    if ((string)error[0]! is "NodeLabelValidationError" or "ValueError")
                    {
                        Assert.Equal((string)error[1]!, thrown.Message);
                    }
                }
                else
                {
                    var actual = PyJson.Dumps(Normalize(Invoke(name, args)));
                    var expected = PyJson.Dumps(result["ok"]);
                    if (actual != expected)
                    {
                        var at = 0;
                        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
                        {
                            at++;
                        }

                        failures.Add($"{label}: the results differ at character {at}: expected ...{expected[Math.Max(0, at - 40)..Math.Min(expected.Length, at + 60)]} but got ...{actual[Math.Max(0, at - 40)..Math.Min(actual.Length, at + 60)]}");
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    private static string PythonName(Exception e) => e switch
    {
        NodeLabelValidationException => "NodeLabelValidationError",
        KeyNotFoundException => "KeyError",
        InvalidCastException => "TypeError",
        ArgumentException => "ValueError",
        _ => e.GetType().Name,
    };

    private static object? Invoke(string name, List<object?> a) => name switch
    {
        "const:NEO4J_TO_FALKORDB_MAPPING" => GraphQueries.Neo4jToFalkordbMapping,
        "const:EPISODIC_EDGE_SAVE" => EdgeDbQueries.EpisodicEdgeSave,
        "const:EPISODIC_EDGE_RETURN" => EdgeDbQueries.EpisodicEdgeReturn,
        "const:COMMUNITY_EDGE_RETURN" => EdgeDbQueries.CommunityEdgeReturn,
        "const:HAS_EPISODE_EDGE_SAVE" => EdgeDbQueries.HasEpisodeEdgeSave,
        "const:HAS_EPISODE_EDGE_RETURN" => EdgeDbQueries.HasEpisodeEdgeReturn,
        "const:NEXT_EPISODE_EDGE_SAVE" => EdgeDbQueries.NextEpisodeEdgeSave,
        "const:NEXT_EPISODE_EDGE_RETURN" => EdgeDbQueries.NextEpisodeEdgeReturn,
        "const:EPISODIC_NODE_RETURN" => NodeDbQueries.EpisodicNodeReturn,
        "const:EPISODIC_NODE_RETURN_NEPTUNE" => NodeDbQueries.EpisodicNodeReturnNeptune,
        "const:COMMUNITY_NODE_RETURN" => NodeDbQueries.CommunityNodeReturn,
        "const:COMMUNITY_NODE_RETURN_NEPTUNE" => NodeDbQueries.CommunityNodeReturnNeptune,
        "const:SAGA_NODE_RETURN" => NodeDbQueries.SagaNodeReturn,
        "const:SAGA_NODE_RETURN_NEPTUNE" => NodeDbQueries.SagaNodeReturnNeptune,
        "get_range_indices" => GraphQueries.GetRangeIndices(Provider(a[0])),
        "get_fulltext_indices" => GraphQueries.GetFulltextIndices(Provider(a[0])),
        "get_nodes_query" => GraphQueries.GetNodesQuery(Str(a[0]), Str(a[1]), Long(a[2]), Provider(a[3])),
        "get_relationships_query" => GraphQueries.GetRelationshipsQuery(Str(a[0]), Long(a[1]), Provider(a[2])),
        "get_vector_cosine_func_query" => GraphQueries.GetVectorCosineFuncQuery(Str(a[0]), Str(a[1]), Provider(a[2])),
        "get_entity_edge_save_query" => a.Count > 1 ? EdgeDbQueries.GetEntityEdgeSaveQuery(Provider(a[0]), Bool(a[1])) : EdgeDbQueries.GetEntityEdgeSaveQuery(Provider(a[0])),
        "get_entity_edge_save_bulk_query" => a.Count > 1 ? EdgeDbQueries.GetEntityEdgeSaveBulkQuery(Provider(a[0]), Bool(a[1])) : EdgeDbQueries.GetEntityEdgeSaveBulkQuery(Provider(a[0])),
        "get_entity_edge_update_bulk_query" => a.Count > 1 ? EdgeDbQueries.GetEntityEdgeUpdateBulkQuery(Provider(a[0]), Bool(a[1])) : EdgeDbQueries.GetEntityEdgeUpdateBulkQuery(Provider(a[0])),
        "get_episodic_edge_save_bulk_query" => EdgeDbQueries.GetEpisodicEdgeSaveBulkQuery(Provider(a[0])),
        "get_entity_edge_return_query" => EdgeDbQueries.GetEntityEdgeReturnQuery(Provider(a[0])),
        "get_community_edge_save_query" => EdgeDbQueries.GetCommunityEdgeSaveQuery(Provider(a[0])),
        "get_episode_node_save_query" => NodeDbQueries.GetEpisodeNodeSaveQuery(Provider(a[0])),
        "get_episode_node_save_bulk_query" => NodeDbQueries.GetEpisodeNodeSaveBulkQuery(Provider(a[0])),
        "get_entity_node_return_query" => NodeDbQueries.GetEntityNodeReturnQuery(Provider(a[0])),
        "get_community_node_save_query" => NodeDbQueries.GetCommunityNodeSaveQuery(Provider(a[0])),
        "get_saga_node_save_query" => NodeDbQueries.GetSagaNodeSaveQuery(Provider(a[0])),
        "get_entity_node_save_query" => a.Count > 2 ? NodeDbQueries.GetEntityNodeSaveQuery(Provider(a[0]), Str(a[1]), Bool(a[2])) : NodeDbQueries.GetEntityNodeSaveQuery(Provider(a[0]), Str(a[1])),
        "get_entity_node_save_bulk_query" => a.Count > 2 ? NodeDbQueries.GetEntityNodeSaveBulkQuery(Provider(a[0]), Nodes(a[1]), Bool(a[2])) : NodeDbQueries.GetEntityNodeSaveBulkQuery(Provider(a[0]), Nodes(a[1])),
        "get_entity_node_update_bulk_query" => a.Count > 2 ? NodeDbQueries.GetEntityNodeUpdateBulkQuery(Provider(a[0]), Nodes(a[1]), Bool(a[2])) : NodeDbQueries.GetEntityNodeUpdateBulkQuery(Provider(a[0]), Nodes(a[1])),
        _ => throw new InvalidOperationException($"No binding for {name}"),
    };

    // The file writes a value that JSON cannot hold as one object: {"$tuple": [...]}, {"$bytes": [ints]}, and
    // {"$pydict": [[key, value], ...]} for a dict with a key that is not a str.
    private static object? Decode(object? value)
    {
        switch (value)
        {
            case List<object?> list:
                return list.Select(Decode).ToList();
            case OrderedDictionary<string, object?> dict when dict.Count == 1 && dict.ContainsKey("$tuple"):
                return PyTuple.From(((List<object?>)dict["$tuple"]!).Select(Decode));
            case OrderedDictionary<string, object?> dict when dict.Count == 1 && dict.ContainsKey("$bytes"):
                return ((List<object?>)dict["$bytes"]!).Select(b => (byte)(long)b!).ToArray();
            case OrderedDictionary<string, object?> dict when dict.Count == 1 && dict.ContainsKey("$pydict"):
                var pydict = new PyDict();
                foreach (var pair in (List<object?>)dict["$pydict"]!)
                {
                    var kv = (List<object?>)pair!;
                    pydict[Decode(kv[0])] = Decode(kv[1]);
                }

                return pydict;
            case OrderedDictionary<string, object?> dict:
                var result = new OrderedDictionary<string, object?>();
                foreach (var (key, entry) in dict)
                {
                    result[key] = Decode(entry);
                }

                return result;
            default:
                return value;
        }
    }

    private static GraphProvider Provider(object? value) => Enum.GetValues<GraphProvider>().Single(p => PyEnum.Name(p) == (string)value!);

    private static string Str(object? value) => (string)value!;

    private static long Long(object? value) => (long)value!;

    private static bool Bool(object? value) => (bool)value!;

    private static List<OrderedDictionary<string, object?>> Nodes(object? value) =>
        ((List<object?>)value!).Cast<OrderedDictionary<string, object?>>().ToList();

    // A result as a value of the model of R6, so that PyJson.Dumps writes it. A tuple is {"$tuple": [...]} as in the file.
    private static object? Normalize(object? value) => value switch
    {
        null or string or bool or long or double => value,
        OrderedDictionary<string, object?> dict => NormalizeDict(dict),
        Dictionary<string, string> map => NormalizeDict(new OrderedDictionary<string, object?>(map.Select(p => KeyValuePair.Create<string, object?>(p.Key, p.Value)))),
        PyTuple tuple => new OrderedDictionary<string, object?> { ["$tuple"] = Enumerable.Range(0, tuple.Count).Select(i => Normalize(tuple[i])).ToList() },
        IEnumerable items => items.Cast<object?>().Select(Normalize).ToList(),
        _ => throw new InvalidOperationException($"No normal form for {value.GetType().Name}"),
    };

    private static OrderedDictionary<string, object?> NormalizeDict(OrderedDictionary<string, object?> dict)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, entry) in dict)
        {
            result[key] = Normalize(entry);
        }

        return result;
    }
}
