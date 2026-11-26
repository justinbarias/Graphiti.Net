// Evaluation test for graphiti_core/llm_client/prompt_config.py (port/RULES.md, R23 and R25).
// values/prompt_config.json holds what port/tools/record_prompt_config.py recorded from Python: generated override and
// route trees, the group classes, the checks of LLMTransport and LLMModel with the text of each error, and the
// comparison of a set of models.
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using System.Runtime.CompilerServices;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;

// A model client that the tests build with no constructor call: the constructor of LLMClient belongs to another unit.
// The runtime reads only PythonName of it.
internal class LlmClientPromptConfigFakeLlm : LLMClient
{
    protected LlmClientPromptConfigFakeLlm()
        : base(null)
    {
    }

    internal override string PythonName => "FakeLLM";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderedDictionary<string, object?>());

    internal static LlmClientPromptConfigFakeLlm Create() =>
        (LlmClientPromptConfigFakeLlm)RuntimeHelpers.GetUninitializedObject(typeof(LlmClientPromptConfigFakeLlm));
}

internal sealed class LlmClientPromptConfigOtherLlm : LlmClientPromptConfigFakeLlm
{
    internal override string PythonName => "OtherLLM";

    internal static new LlmClientPromptConfigOtherLlm Create() =>
        (LlmClientPromptConfigOtherLlm)RuntimeHelpers.GetUninitializedObject(typeof(LlmClientPromptConfigOtherLlm));
}

public class LlmClientPromptConfigValuesTests
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private static readonly Dictionary<string, ChatPromptFunction> Builders = [];

    private static readonly Dictionary<string, LLMClient> Clients = [];

    private static readonly Dictionary<string, LLMTransport> Transports = [];

    private static readonly Dictionary<string, LLMModel> Models = BuildPool();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "prompt_config.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, object?> Dict(object? value) => Py.AsDict(value);

    private static List<object?> Items(object? value) => Py.AsList(value);

    private static string Pascal(string name) =>
        string.Concat(name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static Type GroupType(Type parent, string group) =>
        parent.GetNestedType(Pascal(group) + "Group", BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException($"{parent.Name} has no group {group}");

    private static void SetField(object instance, string pythonName, object? value) =>
        (instance.GetType().GetProperty(Pascal(pythonName), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{instance.GetType().Name} has no field {pythonName}"))
        .SetValue(instance, value);

    private static ChatPromptFunction Builder(string marker)
    {
        if (!Builders.TryGetValue(marker, out var function))
        {
            // The closure keeps the builders of two markers apart: a lambda with no capture is one cached delegate.
            function = _ => throw new InvalidOperationException(marker);
            Builders[marker] = function;
        }

        return function;
    }

    private static string MarkerOf(ChatPromptFunction function) =>
        Builders.Single(entry => ReferenceEquals(entry.Value, function)).Key;

    private static string ModelNameOf(LLMModel model) =>
        Models.Single(entry => ReferenceEquals(entry.Value, model)).Key;

    private static LLMPromptOverrides BuildOverrides(OrderedDictionary<string, object?> spec)
    {
        var overrides = new LLMPromptOverrides();
        foreach (var (group, methodsValue) in spec)
        {
            var methods = Dict(methodsValue);
            var instance = Activator.CreateInstance(GroupType(typeof(LLMPromptOverrides), methods.TryGetValue("$class", out var cls) ? Py.AsString(cls) : group), nonPublic: true)
                ?? throw new InvalidOperationException(group);
            foreach (var (name, value) in methods)
            {
                if (name != "$class")
                {
                    SetField(instance, name, Builder(Py.AsString(value)));
                }
            }

            SetField(overrides, group, instance);
        }

        return overrides;
    }

    private static PromptRoutes BuildRoutes(OrderedDictionary<string, object?> spec)
    {
        var routes = new PromptRoutes();
        foreach (var (group, value) in spec)
        {
            if (value is string modelName)
            {
                SetField(routes, group, Models[modelName]);
            }
            else if (Dict(value) is var fields && (fields.ContainsKey("$value") || fields.ContainsKey("$model")))
            {
                SetField(routes, group, fields.TryGetValue("$value", out var raw) ? raw : Models[Py.AsString(fields["$model"])]);
            }
            else
            {
                var instance = Activator.CreateInstance(GroupType(typeof(PromptRoutes), fields.TryGetValue("$class", out var cls) ? Py.AsString(cls) : group), nonPublic: true)
                    ?? throw new InvalidOperationException(group);
                foreach (var (name, item) in fields)
                {
                    if (name != "$class")
                    {
                        // A model name, or the value None (the only raw value that the C# property can hold).
                        SetField(instance, name, item is string modelName2 ? Models[modelName2] : Dict(item)["$value"]);
                    }
                }

                SetField(routes, group, instance);
            }
        }

        return routes;
    }

    private static Dictionary<string, LLMModel> BuildPool()
    {
        foreach (var item in Items(Document["clients"]))
        {
            var client = Dict(item);
            Clients[Py.AsString(client["name"])] = Py.AsString(client["class"]) == "FakeLLM"
                ? LlmClientPromptConfigFakeLlm.Create()
                : LlmClientPromptConfigOtherLlm.Create();
        }

        foreach (var item in Items(Document["transports"]))
        {
            var transport = Dict(item);
            var models = transport["models"] is null ? null : Items(transport["models"]).Select(Py.AsString).ToList();
            Transports[Py.AsString(transport["name"])] = new LLMTransport(Clients[Py.AsString(transport["client"])], models);
        }

        Dictionary<string, LLMModel> pool = [];
        foreach (var item in Items(Document["models"]))
        {
            var model = Dict(item);
            pool[Py.AsString(model["name"])] = Transports[Py.AsString(model["transport"])].Model(
                Py.AsString(model["id"]),
                model.TryGetValue("overrides", out var overrides) ? BuildOverrides(Dict(overrides)) : null,
                model.TryGetValue("max_tokens", out var maxTokens) ? Py.AsLong(maxTokens) : null);
        }

        return pool;
    }

    // The exception type that carries each Python error class (RULES.md R4).
    private static Type ErrorType(string pythonClass) => pythonClass switch
    {
        "ValueError" => typeof(ArgumentException),
        "TypeError" => typeof(InvalidCastException),
        "AttributeError" => typeof(MissingMemberException),
        _ => throw new InvalidOperationException(pythonClass),
    };

    // Runs the call, and compares the result or the error with the record of Python.
    private static void Check(object? recorded, Func<object?> call, Func<object?, object?> describe, string label)
    {
        var result = Dict(recorded);
        object? actual;
        try
        {
            actual = describe(call());
        }
        catch (Exception error) when (result.ContainsKey("err"))
        {
            var expected = Items(result["err"]);
            Assert.True(
                error.GetType() == ErrorType(Py.AsString(expected[0])) && error.Message == Py.AsString(expected[1]),
                $"{label}: expected {expected[0]}: {expected[1]} but got {error.GetType().Name}: {error.Message}");
            return;
        }

        if (!result.TryGetValue("ok", out var ok))
        {
            Assert.Fail($"{label}: Python raised {PyJson.Dumps(result["err"])}, and the port returned {PyJson.Dumps(actual)}");
        }

        Assert.True(PyJson.Dumps(actual) == PyJson.Dumps(ok), $"{label}: expected {PyJson.Dumps(ok)} but got {PyJson.Dumps(actual)}");
    }

    [Fact]
    public void The_prompt_groups_that_the_flatten_functions_read_are_the_groups_of_Python()
    {
        var recorded = Dict(Document["groups"]);
        Assert.Equal(recorded.Keys.ToList(), Lib.PromptGroups.Keys.ToList());
        foreach (var (group, names) in recorded)
        {
            Assert.Equal(Items(names).Select(Py.AsString).ToList(), Lib.PromptGroups[group].ToList());
        }
    }

    [Fact]
    public void flatten_overrides_gives_each_recorded_result_of_Python()
    {
        var cases = Items(Document["overrides"]);
        Assert.True(cases.Count >= 250);
        var skipped = 0;
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            if (recorded["cs"] is false)
            {
                // The fields of LLMPromptOverrides have the types of the annotations: a wrong group class and a value
                // that is not callable have no C# form (R22).
                skipped++;
                continue;
            }

            var spec = Dict(recorded["spec"]);
            Check(
                recorded["r"],
                () => PromptConfig.FlattenOverrides(BuildOverrides(spec)),
                flat => ((OrderedDictionary<string, ChatPromptFunction>)flat!).Select(entry => new List<object?> { entry.Key, MarkerOf(entry.Value) }).ToList(),
                PyJson.Dumps(spec));
        }

        Assert.True(skipped >= 100, "The recording holds the cases that have no C# form.");
    }

    [Fact]
    public void flatten_routes_gives_each_recorded_result_of_Python()
    {
        var cases = Items(Document["routes"]);
        Assert.True(cases.Count >= 350);
        var skipped = 0;
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            if (recorded["cs"] is false)
            {
                // A nested value that is not an LLMModel: the C# properties hold LLMModel?.
                skipped++;
                continue;
            }

            var spec = Dict(recorded["spec"]);
            Check(
                recorded["r"],
                () => PromptConfig.FlattenRoutes(BuildRoutes(spec)),
                flat => ((OrderedDictionary<string, LLMModel>)flat!).Select(entry => new List<object?> { entry.Key, ModelNameOf(entry.Value) }).ToList(),
                PyJson.Dumps(spec));
        }

        Assert.True(skipped >= 20, "The recording holds the cases that have no C# form.");
    }

    [Fact]
    public void _nested_group_class_gives_each_recorded_class_name_and_error_of_Python()
    {
        var cases = Items(Document["nested"]);
        Assert.True(cases.Count >= 50);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var parentName = Py.AsString(recorded["parent"]);
            var parent = parentName == "LLMPromptOverrides" ? typeof(LLMPromptOverrides) : typeof(PromptRoutes);
            var group = Py.AsString(recorded["group"]);
            Check(
                recorded["r"],
                () => PromptConfig.NestedGroupClass(parent, group),
                type => parentName + "." + (string?)((Type)type!).GetField("PythonName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetRawConstantValue(),
                $"{parentName} {PyJson.Dumps(group)}");
        }
    }

    [Fact]
    public void LLMTransport_gives_each_recorded_model_set_and_error_of_Python()
    {
        var cases = Items(Document["transport_cases"]);
        Assert.True(cases.Count >= 100);
        var firstOk = cases.Select(c => Dict(Dict(c)["r"])).First(r => r.ContainsKey("ok"));
        var supported = Dict(Dict(firstOk["ok"])["supports"]).Keys.ToList();
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var models = recorded["models"] is null ? null : Items(recorded["models"]).Select(Py.AsString).ToList();
            var client = Clients[Py.AsString(recorded["client"])];
            Check(
                recorded["r"],
                () => new LLMTransport(client, models),
                created =>
                {
                    var transport = (LLMTransport)created!;
                    OrderedDictionary<string, object?> supports = [];
                    foreach (var id in supported)
                    {
                        supports[id] = transport.Supports(id);
                    }

                    return new OrderedDictionary<string, object?>
                    {
                        ["models"] = transport.Models?.Order(Py.StringComparer).Cast<object?>().ToList(),
                        ["repr"] = transport.Repr(),
                        ["supports"] = supports,
                    };
                },
                PyJson.Dumps(models));
        }
    }

    [Fact]
    public void LLMModel_gives_each_recorded_check_and_error_of_Python_on_each_construction_path()
    {
        var cases = Items(Document["model_cases"]);
        Assert.True(cases.Count >= 150);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var transport = Transports[Py.AsString(recorded["transport"])];
            var id = Py.AsString(recorded["id"]);
            var maxTokens = recorded["max_tokens"] is null ? (long?)null : Py.AsLong(recorded["max_tokens"]);
            var overrides = recorded["overrides"] is null ? null : BuildOverrides(Dict(recorded["overrides"]));
            Func<object?, object?> describe = created =>
            {
                var model = (LLMModel)created!;
                return new OrderedDictionary<string, object?>
                {
                    ["id"] = model.Id,
                    ["max_tokens"] = model.MaxTokens,
                    ["flat"] = model.FlatOverrides.Select(entry => new List<object?> { entry.Key, MarkerOf(entry.Value) }).ToList(),
                };
            };
            var label = PyJson.Dumps(recorded);

            // transport.model(id, prompt_overrides=..., max_tokens=...)
            Check(recorded["r"], () => transport.Model(id, overrides, maxTokens), describe, label);

            // LLMModel(id=..., transport=..., prompt_overrides=..., max_tokens=...)
            Check(recorded["r"], () => new LLMModel { Id = id, Transport = transport, PromptOverrides = overrides, MaxTokens = maxTokens }.Validate(), describe, label);
        }
    }

    [Fact]
    public void A_model_compares_and_hashes_as_Python_does()
    {
        var equality = Dict(Document["equality"]);
        var names = Items(equality["names"]).Select(Py.AsString).ToList();
        var eq = Items(equality["eq"]);
        var hashEq = Items(equality["hash_eq_when_equal"]);
        var equalPairs = 0;
        for (var a = 0; a < names.Count; a++)
        {
            for (var b = 0; b < names.Count; b++)
            {
                var expected = (bool)Items(eq[a])[b]!;
                Assert.True(expected == Models[names[a]].Equals(Models[names[b]]), $"{names[a]} == {names[b]}");
                Assert.True(expected == (Models[names[a]] == Models[names[b]]), $"{names[a]} == {names[b]}");
                if (expected)
                {
                    equalPairs++;
                    Assert.True((bool)Items(hashEq[a])[b]!, $"The recording needs hash({names[a]}) == hash({names[b]})");
                    Assert.True(Models[names[a]].GetHashCode() == Models[names[b]].GetHashCode(), $"hash({names[a]}) == hash({names[b]})");
                }
            }
        }

        Assert.True(equalPairs >= 14);
    }
}
