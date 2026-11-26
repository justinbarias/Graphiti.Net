// Evaluation test for graphiti_core/prompts/eval.py, summarize_nodes.py, summarize_sagas.py, and names.py (R23, R25).
// It compares the port with the results that port/tools/record_prompts_eval_summaries.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

public class PromptsEvalSummariesRecordedTests
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
        Assert.Equal(1532, Cases.Count);
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

    private static object? Run(string fn, object? args)
    {
        switch (fn)
        {
            case "QueryExpansion":
                return QueryExpansion.ModelValidate(Context(args)).ModelDump();
            case "QAResponse":
                return QAResponse.ModelValidate(Context(args)).ModelDump();
            case "EvalResponse":
                return EvalResponse.ModelValidate(Context(args)).ModelDump();
            case "EvalAddEpisodeResults":
                return EvalAddEpisodeResults.ModelValidate(Context(args)).ModelDump();
            case "Summary":
                return Summary.ModelValidate(Context(args)).ModelDump();
            case "SummaryDescription":
                return SummaryDescription.ModelValidate(Context(args)).ModelDump();
            case "SagaSummary":
                return SagaSummary.ModelValidate(Context(args)).ModelDump();
            case "schema":
                var schemas = new OrderedDictionary<string, PydanticSchema>
                {
                    ["QueryExpansion"] = QueryExpansion.ModelJsonSchema,
                    ["QAResponse"] = QAResponse.ModelJsonSchema,
                    ["EvalResponse"] = EvalResponse.ModelJsonSchema,
                    ["EvalAddEpisodeResults"] = EvalAddEpisodeResults.ModelJsonSchema,
                    ["Summary"] = Summary.ModelJsonSchema,
                    ["SummaryDescription"] = SummaryDescription.ModelJsonSchema,
                    ["SagaSummary"] = SagaSummary.ModelJsonSchema,
                };
                return PyJson.Dumps(schemas[(string)args!].ToDict());
            case "names":
                return (string)args! == "PromptGroup"
                    ? Names.PromptGroup.Cast<object?>().ToList()
                    : Names.PromptName.Cast<object?>().ToList();
            case "versions_keys":
                var versions = (string)args! switch
                {
                    "eval" => Eval.Versions.Keys,
                    "summarize_nodes" => SummarizeNodes.Versions.Keys,
                    _ => SummarizeSagas.Versions.Keys,
                };
                return Py.Repr(versions.Cast<object?>().ToList());
        }

        var (kind, name) = fn.Split('.', 2) is [var k, var n] ? (k, n) : (fn, string.Empty);
        var context = Context(args);
        var versionsTable = name switch
        {
            "summarize_pair" or "summarize_context" or "summary_description" => SummarizeNodes.Versions,
            "summarize_saga" => SummarizeSagas.Versions,
            _ => Eval.Versions,
        };
        return kind switch
        {
            "versions" => versionsTable[name](context),
            "default" => Default(name, context),
            "default_as_messages" => Default(name, context).AsMessages(),
            "default_as_messages_false" => Default(name, context).AsMessages(false),
            "getattr" => Group(name).TryGetFunction(name, out var function) ? function!(context) : throw new InvalidOperationException($"no prompt {name}"),
            "getattr_unknown" => GroupOf(name).TryGetFunction((string)context["name"]!, out _) ? throw new InvalidOperationException($"found {context["name"]}") : "AttributeError",
            _ => PlainFunction(fn, context),
        };
    }

    private static List<Message> PlainFunction(string fn, OrderedDictionary<string, object?> context) => fn switch
    {
        "query_expansion" => Eval.QueryExpansion(context),
        "qa_prompt" => Eval.QaPrompt(context),
        "eval_prompt" => Eval.EvalPrompt(context),
        "eval_add_episode_results" => Eval.EvalAddEpisodeResults(context),
        "summarize_pair" => SummarizeNodes.SummarizePair(context),
        "summarize_context" => SummarizeNodes.SummarizeContext(context),
        "summary_description" => SummarizeNodes.SummaryDescription(context),
        "summarize_saga" => SummarizeSagas.SummarizeSaga(context),
        _ => throw new InvalidOperationException($"no binding for {fn}"),
    };

    private static IPromptGroup Group(string name) => name switch
    {
        "summarize_pair" or "summarize_context" or "summary_description" => new DefaultSummarizeNodesPrompts(),
        "summarize_saga" => new DefaultSummarizeSagasPrompts(),
        _ => new DefaultEvalPrompts(),
    };

    private static IPromptGroup GroupOf(string module) => module switch
    {
        "summarize_nodes" => new DefaultSummarizeNodesPrompts(),
        "summarize_sagas" => new DefaultSummarizeSagasPrompts(),
        _ => new DefaultEvalPrompts(),
    };

    private static ChatPrompt Default(string name, OrderedDictionary<string, object?> context) => name switch
    {
        "query_expansion" => new DefaultEvalPrompts().QueryExpansion(context),
        "qa_prompt" => new DefaultEvalPrompts().QaPrompt(context),
        "eval_prompt" => new DefaultEvalPrompts().EvalPrompt(context),
        "eval_add_episode_results" => new DefaultEvalPrompts().EvalAddEpisodeResults(context),
        "summarize_pair" => new DefaultSummarizeNodesPrompts().SummarizePair(context),
        "summarize_context" => new DefaultSummarizeNodesPrompts().SummarizeContext(context),
        "summary_description" => new DefaultSummarizeNodesPrompts().SummaryDescription(context),
        _ => new DefaultSummarizeSagasPrompts().SummarizeSaga(context),
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
            case IResponseModel model:
                return Tag("class", model.PythonName);
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompts_eval_summaries.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }
}
