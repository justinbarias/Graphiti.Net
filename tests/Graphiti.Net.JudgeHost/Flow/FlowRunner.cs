namespace Graphiti.JudgeHost.Flow;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.Neo4j;
using Graphiti.PyCompat;
using global::Neo4j.Driver;

/// <summary>
/// Runs one flow case: <c>run_flow</c> of <c>port/judge/host-python/host.py</c>, step for step. It prepares the
/// database, puts the recording driver into <c>Neo4jDriver.Client</c>, builds <see cref="GraphitiCore"/> with the judge
/// clients, runs each step, dumps the graph of the groups, and deletes the groups also after a step that throws. The
/// runner applies the noise rules: this class writes what the port gave.
/// </summary>
internal static class FlowRunner
{
    internal static async ValueTask<JsonNode> RunAsync(JsonElement scenario)
    {
        var recorder = new FlowRecorder();
        List<string> groups = [scenario.GetProperty("group").GetString()!];
        if (scenario.TryGetProperty("extra_groups", out var extra))
        {
            groups.AddRange(extra.EnumerateArray().Select(g => g.GetString()!));
        }

        string? repliesPath = scenario.TryGetProperty("replies", out var replies) && replies.ValueKind == JsonValueKind.String && replies.GetString() is { Length: > 0 } name
            ? Path.Combine(Directory.GetCurrentDirectory(), "port", "judge", "replies", name + ".jsonl")
            : null;

        // The defaults of tests/Graphiti.Net.E2E.Tests/Neo4jSidecar.cs and of host.py.
        var raw = GraphDatabase.Driver(
            Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687",
            AuthTokens.Basic(
                Environment.GetEnvironmentVariable("NEO4J_USER") ?? "neo4j",
                Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "password"));
        var driver = new Neo4jDriver(raw);
        await PrepareDatabaseAsync(driver, raw, groups);
        driver.Client = new RecordingDriver(raw, recorder);

        List<object?> returns = [];
        GraphitiCore? graphiti = null;
        var decoder = new FlowDecoder(Bindings.Registry, returns, () => graphiti);
        var keywords = new OrderedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["graph_driver"] = driver,
            ["llm_client"] = new ChatClientLlmClient(
                new FlowChatClient(recorder, repliesPath),
                new LLMConfig(apiKey: "judge", model: "judge-medium", smallModel: "judge-small")),
            ["embedder"] = new EmbeddingGeneratorEmbedder(new FlowEmbeddingGenerator(recorder)),
            ["cross_encoder"] = new CrossEncoderBridge(new FlowCrossEncoder(recorder)),
        };
        if (scenario.TryGetProperty("graphiti", out var extras))
        {
            foreach (var (key, value) in (OrderedDictionary<string, object?>)decoder.Decode(extras)!)
            {
                keywords[key] = value;
            }
        }

        graphiti = FlowCalls.Construct(keywords);

        var entries = new List<(JsonObject Entry, StepCapture Capture)>();
        List<JsonNode> missing = [];
        JsonObject graph;
        try
        {
            foreach (var step in scenario.GetProperty("steps").EnumerateArray())
            {
                var capture = recorder.Reset();
                var entry = new JsonObject { ["call"] = step.TryGetProperty("call", out var call) ? call.GetString() : "parallel" };
                try
                {
                    object? value;
                    if (step.TryGetProperty("parallel", out var parallel))
                    {
                        // Calls that start together. The result keeps the order of the list.
                        var calls = parallel.EnumerateArray().Select(part => Bind(graphiti, part, decoder)).ToList();
                        value = await Asyncio.GatherAsync(calls.Select(c => (Func<Task<object?>>)(() => FlowCalls.InvokeAsync(c))).ToList());
                    }
                    else
                    {
                        value = await FlowCalls.InvokeAsync(Bind(graphiti, step, decoder));
                    }

                    returns.Add(value);
                    entry["ret"] = Codec.Encode(value);
                }
#pragma warning disable CA1031 // host.py catches each exception of a step and writes its class name.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    if (Environment.GetEnvironmentVariable("JUDGE_VERBOSE") is { Length: > 0 })
                    {
                        await Console.Error.WriteLineAsync(error.ToString());
                    }

                    returns.Add(null);
                    entry["error"] = ErrorName(error);
                }

                missing.AddRange(recorder.Missing);
                entries.Add((entry, capture));
            }

            graph = await DumpGraphAsync(raw, groups);
        }
        finally
        {
            await DeleteGroupsAsync(raw, groups);
            await raw.DisposeAsync();
        }

        var steps = new JsonArray();
        foreach (var (entry, capture) in entries)
        {
            foreach (var (key, value) in recorder.Take(capture))
            {
                entry[key] = value?.DeepClone();
            }

            steps.Add(entry);
        }

        var result = new JsonObject
        {
            ["steps"] = steps,
            ["graph"] = graph,
            ["meta"] = new JsonObject
            {
                ["start"] = recorder.Started.IsoFormat(),
                ["end"] = PyDateTime.FromDateTimeOffset(DateTimeOffset.UtcNow).IsoFormat(),
            },
        };
        if (missing.Count > 0)
        {
            result["missing_replies"] = new JsonArray([.. missing.Select(m => m.DeepClone())]);
        }

        return result;
    }

    /// <summary>The Python class name of a step error. A .NET type with no Python name is written with its .NET name.</summary>
    internal static string ErrorName(Exception error) => error switch
    {
        JudgeMissingReplyException => "JudgeMissingReply",
        JudgeNotBoundException => Bindings.NotBound,
        JudgeTypeRejectedException => Bindings.TypeRejected,
        _ => Codec.PythonExceptionName(error) ?? "dotnet:" + error.GetType().FullName,
    };

    // target = _lookup(graphiti, call), then the args and the kwargs.
    private static FlowCalls.BoundCall Bind(GraphitiCore graphiti, JsonElement step, FlowDecoder decoder)
    {
        var path = step.GetProperty("call").GetString()!;
        var positional = step.TryGetProperty("args", out var args) ? decoder.Decode(args) : new List<object?>();
        var keywords = step.TryGetProperty("kwargs", out var kwargs) ? decoder.Decode(kwargs) : new OrderedDictionary<string, object?>();
        return FlowCalls.Bind(
            graphiti,
            path,
            positional as List<object?> ?? throw new InvalidCastException("argument after * must be a list"),
            keywords as OrderedDictionary<string, object?> ?? throw new InvalidCastException("argument after ** must be a mapping"));
    }

    // _prepare_database: an empty database with new full-text indexes. Not captured.
    private static async Task PrepareDatabaseAsync(Neo4jDriver driver, IDriver raw, List<string> groups)
    {
        await DeleteGroupsAsync(raw, groups);
        var count = (await QueryAsync(raw, "MATCH (n) RETURN count(n) AS count", null))[0]["count"].As<long>();
        if (count != 0)
        {
            await Console.Error.WriteLineAsync(
                $"judge: the database holds {count} nodes outside the groups of this case. "
                + "The judge needs a database of its own: no other test or tool can use it at the same time.");
            throw new JudgeDatabaseNotEmpty();
        }

        foreach (var row in await QueryAsync(raw, "SHOW FULLTEXT INDEXES YIELD name RETURN name", null))
        {
            await QueryAsync(raw, $"DROP INDEX `{row["name"].As<string>()}` IF EXISTS", null);
        }

        await driver.BuildIndicesAndConstraintsAsync();
        await QueryAsync(raw, "CALL db.awaitIndexes(120)", null);
    }

    private static async Task DeleteGroupsAsync(IDriver raw, List<string> groups) =>
        await QueryAsync(raw, "MATCH (n) WHERE n.group_id IN $groups DETACH DELETE n", groups);

    // _dump_graph: the nodes and the edges of the groups, with sorted labels and encoded properties.
    private static async Task<JsonObject> DumpGraphAsync(IDriver raw, List<string> groups)
    {
        var nodes = await QueryAsync(raw, "MATCH (n) WHERE n.group_id IN $groups RETURN labels(n) AS labels, properties(n) AS props", groups);
        var edges = await QueryAsync(
            raw,
            "MATCH (a)-[r]->(b) WHERE r.group_id IN $groups OR a.group_id IN $groups "
            + "RETURN type(r) AS type, a.uuid AS source, b.uuid AS target, properties(r) AS props",
            groups);
        return new JsonObject
        {
            ["nodes"] = new JsonArray([.. nodes.Select(Neo4jDriver.FromDriverRecord).Select(r => (JsonNode)new JsonObject
            {
                ["labels"] = Codec.Encode(((List<object?>)r["labels"]!).Cast<string>().Order(StringComparer.Ordinal).ToList()),
                ["props"] = Codec.Encode(r["props"]),
            })]),
            ["edges"] = new JsonArray([.. edges.Select(Neo4jDriver.FromDriverRecord).Select(r => (JsonNode)new JsonObject
            {
                ["type"] = Codec.Encode(r["type"]),
                ["source"] = Codec.Encode(r["source"]),
                ["target"] = Codec.Encode(r["target"]),
                ["props"] = Codec.Encode(r["props"]),
            })]),
        };
    }

    private static async Task<IReadOnlyList<IRecord>> QueryAsync(IDriver raw, string query, List<string>? groups)
    {
        var executable = raw.ExecutableQuery(query);
        if (groups is not null)
        {
            executable = executable.WithParameters(new Dictionary<string, object> { ["groups"] = groups });
        }

        return (await executable.ExecuteAsync()).Result;
    }
}

/// <summary>
/// The decoder of a flow case: the tags of the codec, and the three tags of <c>_decode_with_steps</c>.
/// <c>$step</c> reads the return of an earlier step, <c>$graphiti</c> an object on the <c>Graphiti</c> instance, and
/// <c>$embed</c> gives the judge embedding of a text.
/// </summary>
internal sealed class FlowDecoder(JudgeRegistry registry, List<object?> returns, Func<object?> graphiti) : ValueDecoder(registry)
{
    protected override bool TryDecodeOther(JsonElement value, out object? result)
    {
        if (Find(value, "$step", out var step))
        {
            var index = step[0].GetInt32();
            result = FlowCalls.Lookup(returns[index < 0 ? index + returns.Count : index], step[1].GetString()!);
            return true;
        }

        if (Find(value, "$graphiti", out var path))
        {
            result = FlowCalls.Lookup(graphiti(), path.GetString()!);
            return true;
        }

        if (Find(value, "$embed", out var text))
        {
            result = JudgeEmbedding.Embed(JudgeJson.ReadString(text)).Select(x => (object?)x).ToList();
            return true;
        }

        result = null;
        return false;
    }
}
