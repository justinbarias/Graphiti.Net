// Evaluation test for graphiti_core/prompts/lib.py (R23, R25). It compares the port with the results that
// port/tools/record_prompts_lib.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.PromptsLibSupport;

public class PromptsLibRecordedTests
{
    private const int CaseCount = 846;

    private static readonly List<JsonElement> Cases = Load();

    // Five models are no response models in upstream, so they have no ResponseModel member (R7). The recording passes
    // each model class as an argument, so the table builds the value for them.
    private static readonly Dictionary<string, IResponseModel> Models = new()
    {
        ["ExtractedEntity"] = ExtractedEntity.ResponseModel,
        ["ExtractedEntities"] = ExtractedEntities.ResponseModel,
        ["EntitySummary"] = new BuiltinResponseModel("EntitySummary", EntitySummary.ModelJsonSchema),
        ["SummarizedEntity"] = new BuiltinResponseModel("SummarizedEntity", SummarizedEntity.ModelJsonSchema),
        ["SummarizedEntities"] = SummarizedEntities.ResponseModel,
        ["NodeDuplicate"] = NodeDuplicate.ResponseModel,
        ["NodeResolutions"] = NodeResolutions.ResponseModel,
        ["Edge"] = new BuiltinResponseModel("Edge", Edge.ModelJsonSchema),
        ["ExtractedEdges"] = ExtractedEdges.ResponseModel,
        ["EdgeTimestamps"] = EdgeTimestamps.ResponseModel,
        ["BatchEdgeTimestamps"] = BatchEdgeTimestamps.ResponseModel,
        ["CombinedEntity"] = new BuiltinResponseModel("CombinedEntity", CombinedEntity.ModelJsonSchema),
        ["CombinedFact"] = new BuiltinResponseModel("CombinedFact", CombinedFact.ModelJsonSchema),
        ["CombinedExtraction"] = CombinedExtraction.ResponseModel,
        ["EdgeDuplicate"] = EdgeDuplicate.ResponseModel,
        ["Summary"] = Summary.ResponseModel,
        ["SummaryDescription"] = SummaryDescription.ResponseModel,
        ["SagaSummary"] = SagaSummary.ResponseModel,
        ["QueryExpansion"] = QueryExpansion.ResponseModel,
        ["QAResponse"] = QAResponse.ResponseModel,
        ["EvalResponse"] = EvalResponse.ResponseModel,
        ["EvalAddEpisodeResults"] = EvalAddEpisodeResults.ResponseModel,
    };

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
        foreach (var fn in new[] { "lib_call", "wrapper_partial", "version_wrapper", "create", "validate", "ensure_wrapped", "get_builder", "ensure_chat_prompt", "resolve", "override_group", "composed", "with_builtin_specs", "const" })
        {
            Assert.Contains(Cases, c => c.GetProperty("fn").GetString() == fn);
        }
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void The_port_gives_the_result_of_Python(int index)
    {
        var recorded = Cases[index];
        var fn = recorded.GetProperty("fn").GetString()!;
        var args = (OrderedDictionary<string, object?>)PyJson.Loads(recorded.GetProperty("args").GetRawText())!;
        var expected = ExpectedText(recorded.GetProperty("expect"));

        if (fn == "const" && (string)args["name"]! == "BUILTIN_PROMPT_SPECS_setitem")
        {
            // `MappingProxyType` raises TypeError for an item assignment: the view has no setter.
            Assert.Contains("\"exc\": \"TypeError\"", expected, StringComparison.Ordinal);
            var view = (IDictionary<string, PromptSpec>)Lib.BuiltinPromptSpecs;
            Assert.Throws<NotSupportedException>(() => view["x"] = Lib.BuiltinPromptSpecs["eval.qa_prompt"]);
            return;
        }

        Assert.Equal(expected, PyJson.Dumps(Run(fn, args)));
    }

    private static object? Run(string fn, OrderedDictionary<string, object?> args) => fn switch
    {
        "lib_call" => LibCall(args),
        "wrapper_partial" => WrapperPartial(args),
        "version_wrapper" => VersionWrapperCall(args),
        "create" => Create(args),
        "validate" => Validate(args),
        "ensure_wrapped" => EnsureWrapped(args),
        "get_builder" => GetBuilder(args),
        "ensure_chat_prompt" => EnsureChatPromptCall(args),
        "resolve" => Resolve(args),
        "override_group" => OverrideGroupCall(args),
        "composed" => Composed(args),
        "with_builtin_specs" => WithBuiltinSpecs(args),
        "const" => Const((string)args["name"]!),
        _ => throw new InvalidOperationException($"no binding for {fn}"),
    };

    // ---- the payload helpers: the mirror of enc_result, exc_payload, and probe of the recording tool ----

    private static object? Attempt(Func<object?> body)
    {
        try
        {
            return Enc(body());
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            return ExceptionPayload(e);
        }
    }

    private static OrderedDictionary<string, object?> ExceptionPayload(Exception e)
    {
        var name = Codec.PythonExceptionName(e);
        var payload = new OrderedDictionary<string, object?> { ["exc"] = name };
        if (name is "ValueError" or "TypeError")
        {
            payload["msg"] = e.Message;
        }

        return payload;
    }

    private static object? Enc(object? value) => value switch
    {
        List<Message> messages => messages.Select(m => (object?)new List<object?> { m.Role, m.Content }).ToList(),
        ChatPrompt prompt => new OrderedDictionary<string, object?>
        {
            ["system"] = prompt.System.Content,
            ["user"] = prompt.User.Content,
            ["srole"] = prompt.System.Role,
            ["urole"] = prompt.User.Role,
        },
        _ => value,
    };

    private static OrderedDictionary<string, object?> Missing(string what) => new() { ["missing"] = what };

    private static object? Probe(IPromptLibrary library, string group, string function, OrderedDictionary<string, object?> context)
    {
        if (!library.TryGetGroup(group, out var found))
        {
            return Missing("group");
        }

        if (found is null || !found.TryGetFunction(function, out var builder))
        {
            return Missing("function");
        }

        return builder is null ? Missing("callable") : Attempt(() => builder(context));
    }

    private static object? ProbeWrapper(PromptLibrary library, string group, string function, OrderedDictionary<string, object?> context)
    {
        object found;
        try
        {
            found = GroupOf(library, group);
        }
        catch (MissingMemberException)
        {
            return Missing("group");
        }

        PromptFunction builder;
        try
        {
            builder = FunctionOf(found, group, function);
        }
        catch (MissingMemberException)
        {
            return Missing("function");
        }

        return Attempt(() => builder(context));
    }

    private static string TypeName(object? value) => value switch
    {
        DefaultChatPromptLibrary => "DefaultChatPromptLibrary",
        ComposedPromptLibrary => "_ComposedPromptLibrary",
        LibraryWithBuiltinSpecs => "_LibraryWithBuiltinSpecs",
        HandBuiltLibrary => "SimpleNamespace",
        OverrideGroup => "_OverrideGroup",
        _ => value?.GetType().Name ?? "NoneType",
    };

    private static string Str(OrderedDictionary<string, object?> args, string key) => (string)args[key]!;

    private static OrderedDictionary<string, object?> Dict(OrderedDictionary<string, object?> args, string key) =>
        (OrderedDictionary<string, object?>)args[key]!;

    private static List<object?> Items(OrderedDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) && value is List<object?> list ? list : [];

    // ---- the cases ----

    private static object? LibCall(OrderedDictionary<string, object?> args)
    {
        var context = Dict(args, "ctx");
        return Str(args, "lib") == "default"
            ? Probe(Lib.DefaultChatPromptLibrary, Str(args, "group"), Str(args, "function"), context)
            : ProbeWrapper(Lib.PromptLibrary, Str(args, "group"), Str(args, "function"), context);
    }

    private static List<object?> WrapperPartial(OrderedDictionary<string, object?> args)
    {
        var impl = new OrderedDictionary<string, OrderedDictionary<string, PromptFunction>>();
        foreach (var (group, names) in Dict(args, "versions"))
        {
            impl[group] = [];
            foreach (var name in (List<object?>)names!)
            {
                impl[group][(string)name!] = Lib.PromptLibraryImpl[group][(string)name!];
            }
        }

        var wrapper = new PromptLibraryWrapper(impl);
        var context = Dict(args, "ctx");
        return Lib.PromptGroups
            .SelectMany(p => p.Value.Select(f => ProbeWrapper(wrapper, p.Key, f, context)))
            .ToList();
    }

    private static object? VersionWrapperCall(OrderedDictionary<string, object?> args)
    {
        var messages = ((List<object?>)args["messages"]!).Cast<List<object?>>().ToList();
        PromptFunction func = context => messages
            .Select(m => new Message { Role = (string)m[0]!, Content = (string)m[1]! + (string)context["x"]! }.Validate())
            .ToList();
        return Attempt(() => new VersionWrapper(func).Call(Dict(args, "ctx")));
    }

    private static OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> BuildOverrides(OrderedDictionary<string, object?> spec)
    {
        var result = new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>();
        foreach (var (group, functions) in spec)
        {
            result[group] = [];
            foreach (var (function, behavior) in (OrderedDictionary<string, object?>)functions!)
            {
                var o = (OrderedDictionary<string, object?>)behavior!;
                result[group][function] = Override(Str(o, "system"), Str(o, "user_prefix"));
            }
        }

        return result;
    }

    private static OrderedDictionary<string, object?> Create(OrderedDictionary<string, object?> args)
    {
        var context = Dict(args, "ctx");
        IPromptLibrary library;
        try
        {
            library = Lib.CreatePromptLibrary(args["overrides"] is OrderedDictionary<string, object?> spec ? BuildOverrides(spec) : null);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            return ExceptionPayload(e);
        }

        (string, string)[] probes =
        [
            ("extract_nodes", "extract_message"), ("extract_nodes", "extract_text"), ("extract_edges", "edge"),
            ("dedupe_nodes", "node"), ("dedupe_nodes", "nodes"), ("eval", "qa_prompt"), ("eval", "eval_prompt"),
            ("summarize_sagas", "summarize_saga"), ("summarize_nodes", "summarize_pair"),
            ("extract_nodes", "nope"), ("nope", "extract_message"),
        ];
        return new OrderedDictionary<string, object?>
        {
            ["type"] = TypeName(library),
            ["specs_is_builtin"] = ReferenceEquals(library.Specs, Lib.BuiltinPromptSpecs),
            ["probes"] = probes.Select(p => Probe(library, p.Item1, p.Item2, context)).ToList(),
        };
    }

    private static System.Collections.ObjectModel.ReadOnlyDictionary<string, PromptSpec> BuildSpecs(List<object?> mods)
    {
        var d = new OrderedDictionary<string, PromptSpec>();
        foreach (var (name, spec) in Lib.BuiltinPromptSpecs)
        {
            d[name] = new PromptSpec { Name = name, ResponseModel = spec.ResponseModel, DynamicSchema = spec.DynamicSchema };
        }

        foreach (var item in mods)
        {
            var mod = (OrderedDictionary<string, object?>)item!;
            var name = Str(mod, "name");
            switch (Str(mod, "mode"))
            {
                case "drop":
                    d.Remove(name);
                    break;
                case "dyn_flip":
                    d[name] = new PromptSpec { Name = name, ResponseModel = d[name].ResponseModel, DynamicSchema = !d[name].DynamicSchema };
                    break;
                case "model_swap":
                    d[name] = new PromptSpec
                    {
                        Name = name,
                        ResponseModel = mod["to"] is string to ? Models[to] : null,
                        DynamicSchema = d[name].DynamicSchema,
                    };
                    break;
                default:
                    throw new InvalidOperationException("unknown mode");
            }
        }

        return d.AsReadOnly<string, PromptSpec>();
    }

    private static HashSet<string> Names(OrderedDictionary<string, object?> spec, string key) =>
        Items(spec, key).Select(x => (string)x!).ToHashSet();

    private static HashSet<(string, string)> Pairs(OrderedDictionary<string, object?> spec, string key) =>
        Items(spec, key).Select(x => ((string)((List<object?>)x!)[0]!, (string)((List<object?>)x!)[1]!)).ToHashSet();

    private static HandBuiltLibrary BuildFake(OrderedDictionary<string, object?> spec) =>
        HandBuilt(
            Names(spec, "absent"),
            Names(spec, "notgroup"),
            Pairs(spec, "drop"),
            Pairs(spec, "notcallable"),
            spec.TryGetValue("specs", out var specs) && specs is List<object?> mods ? BuildSpecs(mods) : null);

    private static IPromptLibrary NamedLibrary(string name) => name switch
    {
        "default" => Lib.DefaultChatPromptLibrary,
        "created" => Lib.CreatePromptLibrary(Overrides(("extract_nodes", "extract_message", "S", "U "))),
        "created_empty" => Lib.CreatePromptLibrary(
            new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> { ["eval"] = [] }),
        _ => throw new InvalidOperationException(name),
    };

    private static IPromptLibrary LibraryOf(OrderedDictionary<string, object?> args) =>
        args.TryGetValue("fake", out var fake) ? BuildFake((OrderedDictionary<string, object?>)fake!) : NamedLibrary(Str(args, "lib"));

    private static object? Validate(OrderedDictionary<string, object?> args)
    {
        var library = LibraryOf(args);
        return Attempt(() =>
        {
            Lib.ValidatePromptLibrary(library);
            return null;
        });
    }

    private static OrderedDictionary<string, object?> EnsureWrapped(OrderedDictionary<string, object?> args)
    {
        var library = LibraryOf(args);
        IPromptLibrary wrapped;
        try
        {
            wrapped = Lib.EnsurePromptLibraryWrapped(library);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            return ExceptionPayload(e);
        }

        var context = Dict(args, "ctx");
        (string, string)[] probes = args.ContainsKey("fake")
            ? [("extract_nodes", "extract_message"), ("eval", "qa_prompt"), ("nope", "x"), ("eval", "nope")]
            : [("extract_nodes", "extract_message"), ("eval", "qa_prompt")];
        var specs = wrapped.Specs;
        Assert.NotNull(specs);
        return new OrderedDictionary<string, object?>
        {
            ["type"] = TypeName(wrapped),
            ["same"] = ReferenceEquals(wrapped, library),
            ["specs_is_builtin"] = ReferenceEquals(specs, Lib.BuiltinPromptSpecs),
            ["specs_keys"] = specs.Keys.Cast<object?>().ToList(),
            ["probes"] = probes.Select(p => Probe(wrapped, p.Item1, p.Item2, context)).ToList(),
        };
    }

    private static object? GetBuilder(OrderedDictionary<string, object?> args)
    {
        var library = args["lib"] switch
        {
            OrderedDictionary<string, object?> fake => BuildFake((OrderedDictionary<string, object?>)fake["fake"]!),
            _ => GetBuilderLibrary(Str(args, "lib")),
        };
        var context = Dict(args, "ctx");
        return Attempt(() => Lib.GetPromptBuilder(library, Str(args, "name"))(context));
    }

    private static IPromptLibrary GetBuilderLibrary(string name) => name == "default"
        ? Lib.DefaultChatPromptLibrary
        : Lib.CreatePromptLibrary(Overrides(("extract_nodes", "extract_message", "S2", "U2 "), ("eval", "qa_prompt", "S3", "U3 ")));

    private static object? EnsureChatPromptCall(OrderedDictionary<string, object?> args)
    {
        object? value = Str(args, "kind") switch
        {
            "chat_prompt" => Chat("sys", "usr"),
            "list_empty" => new List<object?>(),
            "list_messages" => new List<Message> { new Message { Role = "user", Content = "x" }.Validate() },
            "list_text" => new List<object?> { "a", 1L },
            "str" => "x",
            "str_empty" => string.Empty,
            "none" => null,
            "int" => 5L,
            "float" => 1.5,
            "bool" => true,
            "dict" => new OrderedDictionary<string, object?> { ["system"] = "a" },
            "bytes" => new byte[] { 0x61, 0x62 },
            "message" => new Message { Role = "user", Content = "x" }.Validate(),
            "system_message" => new SystemMessage { Content = "x" }.Validate(),
            _ => throw new InvalidOperationException("unknown kind"),
        };
        var label = Str(args, "label");
        return Attempt(() => (bool)args["private"]! ? Lib.PrivateEnsureChatPrompt(value, label) : Lib.EnsureChatPrompt(value, label));
    }

    private static object? Resolve(OrderedDictionary<string, object?> args) =>
        Attempt(() => Lib.ResolveResponseModel(Str(args, "name"), args["model"] is string model ? Models[model] : null)?.PythonName);

    private static OrderedDictionary<string, ChatPromptFunction> BuildGroupOverrides(OrderedDictionary<string, object?> spec)
    {
        var result = new OrderedDictionary<string, ChatPromptFunction>();
        foreach (var (function, behavior) in spec)
        {
            var o = (OrderedDictionary<string, object?>)behavior!;
            result[function] = Override(Str(o, "system"), Str(o, "user_prefix"));
        }

        return result;
    }

    private static List<object?> OverrideGroupCall(OrderedDictionary<string, object?> args)
    {
        var group = new OverrideGroup(Lib.DefaultChatPromptLibrary.ExtractNodes, BuildGroupOverrides(Dict(args, "overrides")));
        var context = Dict(args, "ctx");
        return ((List<object?>)args["probes"]!)
            .Select(name => group.TryGetFunction((string)name!, out var function) && function is not null
                ? Attempt(() => function(context))
                : new OrderedDictionary<string, object?> { ["missing"] = true })
            .ToList();
    }

    private static List<object?> Composed(OrderedDictionary<string, object?> args)
    {
        var baseLibrary = new DefaultChatPromptLibrary();
        var composed = new ComposedPromptLibrary(baseLibrary, BuildOverrides(Dict(args, "overrides")));
        var result = new List<object?>();
        foreach (var item in (List<object?>)args["probes"]!)
        {
            var name = (string)item!;
            if (name == "specs")
            {
                result.Add(new OrderedDictionary<string, object?>
                {
                    ["specs_is_base"] = ReferenceEquals(composed.Specs, baseLibrary.Specs),
                    ["keys"] = composed.Specs!.Keys.Cast<object?>().ToList(),
                });
            }
            else if (composed.TryGetGroup(name, out var group))
            {
                Assert.True(baseLibrary.TryGetGroup(name, out var baseGroup));
                result.Add(new OrderedDictionary<string, object?>
                {
                    ["type"] = TypeName(group),
                    ["same_as_base"] = ReferenceEquals(group, baseGroup),
                });
            }
            else
            {
                result.Add(new OrderedDictionary<string, object?> { ["missing"] = true });
            }
        }

        return result;
    }

    private static List<object?> WithBuiltinSpecs(OrderedDictionary<string, object?> args)
    {
        var inner = BuildFake(Dict(args, "fake"));
        IPromptLibrary wrapper = new LibraryWithBuiltinSpecs(inner);
        var result = new List<object?>();
        foreach (var name in Lib.PromptGroups.Keys.Append("specs").Append("nope"))
        {
            if (name == "specs")
            {
                result.Add(new OrderedDictionary<string, object?> { ["specs_is_builtin"] = ReferenceEquals(wrapper.Specs, Lib.BuiltinPromptSpecs) });
            }
            else if (wrapper.TryGetGroup(name, out var group))
            {
                Assert.True(inner.TryGetGroup(name, out var innerGroup));
                result.Add(new OrderedDictionary<string, object?> { ["same_as_inner"] = ReferenceEquals(group, innerGroup) });
            }
            else
            {
                result.Add(new OrderedDictionary<string, object?> { ["missing"] = true });
            }
        }

        return result;
    }

    private static object? Const(string name) => name switch
    {
        "PROMPT_GROUPS" => Ordered(Lib.PromptGroups.Select(p => (p.Key, (object?)p.Value.Cast<object?>().ToList()))),
        "BUILTIN_PROMPT_SPECS" => Lib.BuiltinPromptSpecs
            .Select(p => (object?)new List<object?> { p.Key, p.Value.Name, p.Value.ResponseModel?.PythonName, p.Value.DynamicSchema })
            .ToList(),
        "CHAT_PROMPT_MIGRATION_NOTE" => Lib.ChatPromptMigrationNote,
        "PROMPT_LIBRARY_IMPL" => Ordered(Lib.PromptLibraryImpl.Select(p => (p.Key, (object?)p.Value.Keys.Cast<object?>().ToList()))),
        "DO_NOT_ESCAPE_UNICODE_suffix" => Lib.PromptLibrary.DedupeNodes.Node(PromptsLibTests.PromptContext())
            .Select(m => (object?)m.Content.EndsWith(PromptHelpers.DoNotEscapeUnicode, StringComparison.Ordinal))
            .ToList(),
        _ => throw new InvalidOperationException(name),
    };

    private static OrderedDictionary<string, object?> Ordered(IEnumerable<(string Key, object? Value)> items)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in items)
        {
            result[key] = value;
        }

        return result;
    }

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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompts_lib.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }
}
