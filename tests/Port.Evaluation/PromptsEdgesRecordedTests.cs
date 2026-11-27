// Evaluation test for graphiti_core/prompts/extract_edges.py, dedupe_edges.py, and extract_nodes_and_edges.py (R23, R25).
// It compares the port with the results that port/tools/record_prompts_edges.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

public class PromptsEdgesRecordedTests
{
    private static readonly List<JsonElement> Cases = Load();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void The_recording_has_the_expected_number_of_cases()
    {
        Assert.Equal(CaseCount, Cases.Count);
        Assert.Contains(Cases, c => c.GetProperty("fn").GetString() == "extract_message");
        Assert.Contains(Cases, c => c.GetProperty("fn").GetString() == "Edge" && c.GetProperty("expect").GetProperty("kind").GetString() == "exc");
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void The_port_gives_the_result_of_Python(int index)
    {
        var recorded = Cases[index];
        var id = recorded.GetProperty("id").GetString()!;
        var fn = recorded.GetProperty("fn").GetString()!;
        var args = Untag(PyJson.Loads(recorded.GetProperty("args").GetString()!));
        var expect = recorded.GetProperty("expect");

        object? result = null;
        Exception? error = null;
        try
        {
            result = Run(fn, args);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            error = e;
        }

        if (expect.GetProperty("kind").GetString() == "ok")
        {
            Assert.True(error is null, $"{id}: the port threw {error}");
            Assert.Equal(ExpectedText(expect), PyJson.Dumps(Enc(result)));
            return;
        }

        Assert.True(error is not null, $"{id}: Python raised {expect.GetProperty("cls").GetString()} and the port gave {PyJson.Dumps(Enc(result))}");
        var cls = expect.GetProperty("cls").GetString();
        Assert.Equal(cls, Codec.PythonExceptionName(error));
        if (error is ValidationException validation)
        {
            Assert.Equal(expect.GetProperty("title").GetString(), validation.Title);
            var errors = expect.GetProperty("errors").EnumerateArray().Select(e => PyJson.Dumps(PyJson.Loads(e.GetRawText()))).ToList();
            var actual = validation.Errors
                .Select(e => PyJson.Dumps(new List<object?> { e.Loc.Select(x => x is int i ? (object)(long)i : x).ToList(), e.Type, e.Msg }))
                .ToList();
            Assert.Equal(errors, actual);
        }
        else if (cls == "ValueError" && expect.TryGetProperty("msg", out var msg))
        {
            Assert.Equal(msg.GetString(), error.Message);
        }
    }

    private const int CaseCount = 1031;

    private static object? Run(string fn, object? args)
    {
        switch (fn)
        {
            case "edge":
                return ExtractEdges.Edge(Context(args));
            case "extract_attributes":
                return ExtractEdges.ExtractAttributes(Context(args));
            case "extract_timestamps":
                return ExtractEdges.ExtractTimestamps(Context(args));
            case "extract_timestamps_batch":
                return ExtractEdges.ExtractTimestampsBatch(Context(args));
            case "resolve_edge":
                return DedupeEdges.ResolveEdge(Context(args));
            case "extract_message":
                return ExtractNodesAndEdges.ExtractMessage(Context(args));
            case "_build_edge_types_section":
                var a = Context(args);
                return a.TryGetValue("strict_edge_types", out var strict)
                    ? ExtractNodesAndEdges.BuildEdgeTypesSection(a["edge_types"], strict)
                    : ExtractNodesAndEdges.BuildEdgeTypesSection(a["edge_types"]);
            case "_build_custom_instructions_section":
                return ExtractNodesAndEdges.BuildCustomInstructionsSection((string?)Context(args)["text"]);
            case "Edge":
                return Edge.ModelValidate(Context(args)).ModelDump();
            case "ExtractedEdges":
                return ExtractedEdges.ModelValidate(Context(args)).ModelDump();
            case "EdgeTimestamps":
                return EdgeTimestamps.ModelValidate(Context(args)).ModelDump();
            case "BatchEdgeTimestamps":
                return BatchEdgeTimestamps.ModelValidate(Context(args)).ModelDump();
            case "EdgeDuplicate":
                return EdgeDuplicate.ModelValidate(Context(args)).ModelDump();
            case "CombinedEntity":
                return CombinedEntity.ModelValidate(Context(args)).ModelDump();
            case "CombinedFact":
                return CombinedFact.ModelValidate(Context(args)).ModelDump();
            case "CombinedExtraction":
                return CombinedExtraction.ModelValidate(Context(args)).ModelDump();
            case "schema":
                var schemas = new OrderedDictionary<string, PydanticSchema>
                {
                    ["Edge"] = Edge.ModelJsonSchema,
                    ["ExtractedEdges"] = ExtractedEdges.ModelJsonSchema,
                    ["EdgeTimestamps"] = EdgeTimestamps.ModelJsonSchema,
                    ["BatchEdgeTimestamps"] = BatchEdgeTimestamps.ModelJsonSchema,
                    ["EdgeDuplicate"] = EdgeDuplicate.ModelJsonSchema,
                    ["CombinedEntity"] = CombinedEntity.ModelJsonSchema,
                    ["CombinedFact"] = CombinedFact.ModelJsonSchema,
                    ["CombinedExtraction"] = CombinedExtraction.ModelJsonSchema,
                };
                return PyJson.Dumps(schemas[(string)args!].ToDict());
            case "const":
                return (string)args! switch
                {
                    "_SYSTEM_PROMPT" => ExtractNodesAndEdges.SystemPrompt,
                    "_C2_INSTRUCTION_TEXT" => ExtractNodesAndEdges.C2InstructionText,
                    _ => throw new InvalidOperationException("no constant " + args),
                };
            case "new.Edge":
                var edge = Context(args);
                return new Edge
                {
                    SourceEntityName = (string)edge["source_entity_name"]!,
                    TargetEntityName = (string)edge["target_entity_name"]!,
                    RelationType = (string)edge["relation_type"]!,
                    Fact = (string)edge["fact"]!,
                }.ModelDump();
            case "new.EdgeTimestamps":
                return new EdgeTimestamps().ModelDump();
            case "new.CombinedFact":
                var combined = Context(args);
                return new CombinedFact
                {
                    SourceEntityName = (string)combined["source_entity_name"]!,
                    TargetEntityName = (string)combined["target_entity_name"]!,
                    RelationType = (string)combined["relation_type"]!,
                    Fact = (string)combined["fact"]!,
                }.ModelDump();
            case "versions_keys":
                var versions = (string)args! switch
                {
                    "extract_edges" => ExtractEdges.Versions.Keys,
                    "dedupe_edges" => DedupeEdges.Versions.Keys,
                    _ => ExtractNodesAndEdges.Versions.Keys,
                };
                return Py.Repr(versions.Cast<object?>().ToList());
        }

        var (kind, name) = fn.Split('.', 2) is [var k, var n] ? (k, n) : (fn, string.Empty);
        var context = Context(args);
        return kind switch
        {
            "versions" => Version(name)(context),
            "default" => Default(name, context),
            "default_as_messages" => Default(name, context).AsMessages(),
            "default_as_messages_false" => Default(name, context).AsMessages(false),
            "getattr" => Group(name).TryGetFunction(name, out var function) ? function!(context) : throw new InvalidOperationException($"no prompt {name}"),
            "getattr_unknown" => GroupOf(name).TryGetFunction((string)context["name"]!, out _) ? throw new InvalidOperationException($"found {context["name"]}") : "AttributeError",
            _ => throw new InvalidOperationException($"no binding for {fn}"),
        };
    }

    private static IPromptGroup Group(string name) => name switch
    {
        "resolve_edge" => new DefaultDedupeEdgesPrompts(),
        "extract_message" => new DefaultExtractNodesAndEdgesPrompts(),
        _ => new DefaultExtractEdgesPrompts(),
    };

    private static IPromptGroup GroupOf(string module) => module switch
    {
        "dedupe_edges" => new DefaultDedupeEdgesPrompts(),
        "extract_nodes_and_edges" => new DefaultExtractNodesAndEdgesPrompts(),
        _ => new DefaultExtractEdgesPrompts(),
    };

    private static PromptFunction Version(string name) => name switch
    {
        "resolve_edge" => DedupeEdges.Versions[name],
        "extract_message" => ExtractNodesAndEdges.Versions[name],
        _ => ExtractEdges.Versions[name],
    };

    private static ChatPrompt Default(string name, OrderedDictionary<string, object?> context) => name switch
    {
        "edge" => new DefaultExtractEdgesPrompts().Edge(context),
        "extract_attributes" => new DefaultExtractEdgesPrompts().ExtractAttributes(context),
        "extract_timestamps" => new DefaultExtractEdgesPrompts().ExtractTimestamps(context),
        "extract_timestamps_batch" => new DefaultExtractEdgesPrompts().ExtractTimestampsBatch(context),
        "resolve_edge" => new DefaultDedupeEdgesPrompts().ResolveEdge(context),
        _ => new DefaultExtractNodesAndEdgesPrompts().ExtractMessage(context),
    };

    private static OrderedDictionary<string, object?> Context(object? args) => (OrderedDictionary<string, object?>)args!;

    // The inverse of enc() in the recording tool.
    private static object? Untag(object? value)
    {
        switch (value)
        {
            case OrderedDictionary<string, object?> dict when dict.ContainsKey("$t"):
                var v = dict["v"];
                switch ((string)dict["$t"]!)
                {
                    case "pydict":
                        var pydict = new PyDict();
                        foreach (var pair in (List<object?>)v!)
                        {
                            var kv = (List<object?>)pair!;
                            pydict[Untag(kv[0])] = Untag(kv[1]);
                        }

                        return pydict;
                    case "tuple":
                        return PyTuple.From(((List<object?>)v!).Select(Untag));
                    case "dt":
                        return PyDateTime.FromIsoFormat((string)v!);
                    case "date":
                        return DateOnly.ParseExact((string)v!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    case "bytes":
                        return System.Convert.FromHexString((string)v!);
                    default:
                        throw new InvalidOperationException("unknown tag");
                }

            case OrderedDictionary<string, object?> dict:
                var copy = new OrderedDictionary<string, object?>();
                foreach (var (key, item) in dict)
                {
                    copy[key] = Untag(item);
                }

                return copy;
            case List<object?> list:
                return list.Select(Untag).ToList();
            default:
                return value;
        }
    }

    // enc_result() and enc() of the recording tool.
    private static object? Enc(object? value)
    {
        switch (value)
        {
            case List<Message> messages:
                return messages.Select(m => (object?)new List<object?> { m.Role, m.Content }).ToList();
            case ChatPrompt prompt:
                return new OrderedDictionary<string, object?>
                {
                    ["system"] = prompt.System.Content,
                    ["user"] = prompt.User.Content,
                    ["srole"] = prompt.System.Role,
                    ["urole"] = prompt.User.Role,
                };
            case PyDict pydict:
                return Tag("pydict", pydict.Select(p => (object?)new List<object?> { Enc(p.Key), Enc(p.Value) }).ToList());
            case PyTuple tuple:
                return Tag("tuple", tuple.Select(Enc).ToList());
            case PyDateTime dt:
                return Tag("dt", dt.IsoFormat());
            case DateOnly date:
                return Tag("date", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            case byte[] bytes:
                return Tag("bytes", System.Convert.ToHexStringLower(bytes));
            case OrderedDictionary<string, object?> dict:
                var copy = new OrderedDictionary<string, object?>();
                foreach (var (key, item) in dict)
                {
                    copy[key] = Enc(item);
                }

                return copy;
            case List<object?> list:
                return list.Select(Enc).ToList();
            default:
                return value;
        }
    }

    private static OrderedDictionary<string, object?> Tag(string tag, object? value) => new() { ["$t"] = tag, ["v"] = value };

    private static string ExpectedText(JsonElement expect)
    {
        if (expect.TryGetProperty("text", out var text))
        {
            return text.GetString()!;
        }

        var bytes = System.Convert.FromBase64String(expect.GetProperty("z").GetString()!);
        using var stream = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return reader.ReadToEnd();
    }

    private static List<JsonElement> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompts_edges.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }
}
