// Evaluation test for graphiti_core/prompts/dedupe_nodes.py, models.py, and prompt_helpers.py (R23, R25).
// It compares the port with the results that port/tools/record_prompt_values.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

public class PromptsRecordedTests
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
        Assert.Equal(1209, Cases.Count);
        Assert.Contains(Cases, c => c.GetProperty("fn").GetString() == "to_prompt_json" && c.GetProperty("args").GetString()!.Contains("-1099511627776", StringComparison.Ordinal));
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
            case "to_prompt_json":
                var a = (OrderedDictionary<string, object?>)args!;
                return PromptHelpers.ToPromptJson(
                    a["data"],
                    a.TryGetValue("ensure_ascii", out var ascii) ? (bool)ascii! : false,
                    a.TryGetValue("indent", out var indent) ? (long?)indent : null);
            case "node":
                return DedupeNodes.Node(Context(args));
            case "nodes":
                return DedupeNodes.Nodes(Context(args));
            case "node_list":
                return DedupeNodes.NodeList(Context(args));
            case "Message":
                return Message.ModelValidate(Context(args)).ModelDump();
            case "SystemMessage":
                return SystemMessage.ModelValidate(Context(args)).ModelDump();
            case "UserMessage":
                return UserMessage.ModelValidate(Context(args)).ModelDump();
            case "ChatPrompt":
                return ChatPrompt.ModelValidate(Context(args)).ModelDump();
            case "PromptSpec":
                return PromptSpec.ModelValidate(Context(args)).ModelDump();
            case "NodeDuplicate":
                return NodeDuplicate.ModelValidate(Context(args)).ModelDump();
            case "NodeResolutions":
                return NodeResolutions.ModelValidate(Context(args)).ModelDump();
            case "from_messages":
                return ChatPrompt.FromMessages((List<Message>)args!);
            case "as_messages":
                var b = (OrderedDictionary<string, object?>)args!;
                var prompt = new ChatPrompt
                {
                    System = new SystemMessage { Content = (string)b["system"]! }.Validate(),
                    User = new UserMessage { Content = (string)b["user"]! }.Validate(),
                };
                return b.TryGetValue("flag", out var flag) ? prompt.AsMessages((bool)flag!) : prompt.AsMessages();
            case "schema":
                var schemas = new OrderedDictionary<string, PydanticSchema>
                {
                    ["Message"] = Message.ModelJsonSchema,
                    ["SystemMessage"] = SystemMessage.ModelJsonSchema,
                    ["UserMessage"] = UserMessage.ModelJsonSchema,
                    ["ChatPrompt"] = ChatPrompt.ModelJsonSchema,
                    ["NodeDuplicate"] = NodeDuplicate.ModelJsonSchema,
                    ["NodeResolutions"] = NodeResolutions.ModelJsonSchema,
                };
                return PyJson.Dumps(schemas[(string)args!].ToDict());
            case "const":
                return PromptHelpers.DoNotEscapeUnicode;
            case "versions_keys":
                return Py.Repr(DedupeNodes.Versions.Keys.Cast<object?>().ToList());
        }

        var (kind, name) = fn.Split('.', 2) is [var k, var n] ? (k, n) : (fn, string.Empty);
        var context = Context(args);
        var defaults = new DefaultDedupeNodesPrompts();
        return kind switch
        {
            "versions" => DedupeNodes.Versions[name](context),
            "default" => Default(defaults, name, context),
            "default_as_messages" => Default(defaults, name, context).AsMessages(),
            "default_as_messages_false" => Default(defaults, name, context).AsMessages(false),
            _ => throw new InvalidOperationException($"no binding for {fn}"),
        };
    }

    private static ChatPrompt Default(DefaultDedupeNodesPrompts prompts, string name, OrderedDictionary<string, object?> context) => name switch
    {
        "node" => prompts.Node(context),
        "nodes" => prompts.Nodes(context),
        _ => prompts.NodeList(context),
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
                    case "class":
                        return (string)v! == "NodeDuplicate" ? NodeDuplicate.ResponseModel : NodeResolutions.ResponseModel;
                    case "msgs":
                        return ((List<object?>)v!).Select(m =>
                        {
                            var pair = (List<object?>)m!;
                            return new Message { Role = (string)pair[0]!, Content = (string)pair[1]! }.Validate();
                        }).ToList();
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompts_dedupe_nodes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }
}
