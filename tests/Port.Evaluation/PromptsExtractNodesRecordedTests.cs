// Evaluation test for graphiti_core/prompts/extract_nodes.py and snippets.py (R23, R25).
// It compares the port with the results that port/tools/record_prompts_extract_nodes.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

public class PromptsExtractNodesRecordedTests
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
        Assert.Equal(734, Cases.Count);
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
        Assert.Equal(expect.GetProperty("cls").GetString(), Codec.PythonExceptionName(error));
        if (error is ValidationException validation)
        {
            Assert.Equal(expect.GetProperty("title").GetString(), validation.Title);
            var errors = expect.GetProperty("errors").EnumerateArray().Select(e => PyJson.Dumps(PyJson.Loads(e.GetRawText()))).ToList();
            var actual = validation.Errors
                .Select(e => PyJson.Dumps(new List<object?> { e.Loc.Select(x => x is int i ? (object)(long)i : x).ToList(), e.Type, e.Msg }))
                .ToList();
            Assert.Equal(errors, actual);
        }
    }

    private static object? Run(string fn, object? args)
    {
        switch (fn)
        {
            case "extract_message":
                return ExtractNodes.ExtractMessage(Context(args));
            case "extract_json":
                return ExtractNodes.ExtractJson(Context(args));
            case "extract_text":
                return ExtractNodes.ExtractText(Context(args));
            case "classify_nodes":
                return ExtractNodes.ClassifyNodes(Context(args));
            case "extract_attributes":
                return ExtractNodes.ExtractAttributes(Context(args));
            case "extract_summary":
                return ExtractNodes.ExtractSummary(Context(args));
            case "extract_summaries_batch":
                return ExtractNodes.ExtractSummariesBatch(Context(args));
            case "extract_entity_summaries_from_episodes":
                return ExtractNodes.ExtractEntitySummariesFromEpisodes(Context(args));
            case "_entity_type_descriptions_section":
                return ExtractNodes.EntityTypeDescriptionsSection(Context(args));
            case "ExtractedEntity":
                return ExtractedEntity.ModelValidate(Context(args)).ModelDump();
            case "ExtractedEntities":
                return ExtractedEntities.ModelValidate(Context(args)).ModelDump();
            case "EntitySummary":
                return EntitySummary.ModelValidate(Context(args)).ModelDump();
            case "SummarizedEntity":
                return SummarizedEntity.ModelValidate(Context(args)).ModelDump();
            case "SummarizedEntities":
                return SummarizedEntities.ModelValidate(Context(args)).ModelDump();
            case "new.ExtractedEntity":
                var fields = Context(args);
                return new ExtractedEntity { Name = (string)fields["name"]!, EntityTypeId = (long)fields["entity_type_id"]! }.ModelDump();
            case "schema":
                var schemas = new OrderedDictionary<string, PydanticSchema>
                {
                    ["ExtractedEntity"] = ExtractedEntity.ModelJsonSchema,
                    ["ExtractedEntities"] = ExtractedEntities.ModelJsonSchema,
                    ["EntitySummary"] = EntitySummary.ModelJsonSchema,
                    ["SummarizedEntity"] = SummarizedEntity.ModelJsonSchema,
                    ["SummarizedEntities"] = SummarizedEntities.ModelJsonSchema,
                };
                return PyJson.Dumps(schemas[(string)args!].ToDict());
            case "const":
                return (string)args! switch
                {
                    "summary_instructions" => Snippets.SummaryInstructions,
                    "MAX_SUMMARY_CHARS" => TextUtils.MaxSummaryChars,
                    "_entity_episode_summary_system_prompt" => ExtractNodes.EntityEpisodeSummarySystemPrompt,
                    _ => throw new InvalidOperationException("no constant " + args),
                };
            case "versions_keys":
                return Py.Repr(ExtractNodes.Versions.Keys.Cast<object?>().ToList());
        }

        var (kind, name) = fn.Split('.', 2) is [var k, var n] ? (k, n) : (fn, string.Empty);
        var context = Context(args);
        var defaults = new DefaultExtractNodesPrompts();
        return kind switch
        {
            "versions" => ExtractNodes.Versions[name](context),
            "default" => Default(defaults, name, context),
            "default_as_messages" => Default(defaults, name, context).AsMessages(),
            "getattr" => defaults.TryGetFunction(name, out var function) ? function!(context) : throw new InvalidOperationException($"no prompt {name}"),
            "getattr_unknown" => defaults.TryGetFunction((string)context["name"]!, out _) ? throw new InvalidOperationException($"found {context["name"]}") : "AttributeError",
            _ => throw new InvalidOperationException($"no binding for {fn}"),
        };
    }

    private static ChatPrompt Default(DefaultExtractNodesPrompts prompts, string name, OrderedDictionary<string, object?> context) => name switch
    {
        "extract_message" => prompts.ExtractMessage(context),
        "extract_json" => prompts.ExtractJson(context),
        "extract_text" => prompts.ExtractText(context),
        "classify_nodes" => prompts.ClassifyNodes(context),
        "extract_attributes" => prompts.ExtractAttributes(context),
        "extract_summary" => prompts.ExtractSummary(context),
        "extract_summaries_batch" => prompts.ExtractSummariesBatch(context),
        "extract_entity_summaries_from_episodes" => prompts.ExtractEntitySummariesFromEpisodes(context),
        _ => throw new InvalidOperationException($"no prompt {name}"),
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompts_extract_nodes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }
}
