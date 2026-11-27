// Evaluation test for graphiti_core/llm_client/llm_runtime.py (port/RULES.md, R23 and R25).
// values/llm_runtime.json holds what port/tools/record_llm_runtime.py recorded from Python: the constructor and its
// transports, set_tracer, resolve_model and resolve_builder for each prompt name, complete with the calls that the fake
// client received and each error, calls that run at the same time, and _wrap_builder. The fake client sits at the
// network boundary: each fake method yields first and then records its call into one list, together with the log lines.
//
// Two facts of the port that the recording cannot hold:
// - `LLMClient.SetTracer` is not virtual, so a test cannot count its calls. The test compares which clients hold the
//   tracer. The recording has the Python count, and the test checks that Python called each client one time.
// - A builder that returns a value that is not a ChatPrompt has no C# form (the delegate type holds the class), so the
//   `cs: false` cases of _wrap_builder are counted and not run.
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// The model client at the network boundary. It yields first, then records the call (R25).
internal sealed class LlmRuntimeFake(string name, List<object?> events) : LLMClient(new LLMConfig(model: "gpt-4.1"), false)
{
    internal string Name { get; } = name;

    internal override string PythonName => "FakeLLM";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderedDictionary<string, object?>());

    internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        events.Add(new List<object?>
        {
            "call",
            Name,
            new OrderedDictionary<string, object?>
            {
                ["model"] = model,
                ["prompt_name"] = promptName,
                ["response_model"] = responseModel?.PythonName,
                ["max_tokens"] = maxTokens,
                ["model_size"] = Py.EnumValue(modelSize),
                ["group_id"] = groupId,
                ["attribute_extraction"] = attributeExtraction,
                ["messages"] = LlmClientClientSupport.Pairs(messages),
            },
        });
        if (model == "fail")
        {
            throw new ArgumentException("fake client failed: fail");
        }

        if (model == "failrt")
        {
            throw new InvalidOperationException("fake client failed: failrt");
        }

        return new OrderedDictionary<string, object?> { ["client"] = Name, ["model"] = model };
    }
}

public class LlmClientRuntimeValuesTests
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private static readonly IResponseModel OtherModel = new BuiltinResponseModel("OtherModel", EntitySummary.ModelJsonSchema);

    private static readonly IResponseModel PersonAttrs = new BuiltinResponseModel("PersonAttrs", EntitySummary.ModelJsonSchema);

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "llm_runtime.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, object?> Dict(object? value) => Py.AsDict(value);

    private static List<object?> Items(object? value) => Py.AsList(value);

    private static string Str(object? value) => Py.AsString(value);

    private static string Pascal(string name) =>
        string.Concat(name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static Type GroupType(Type parent, string group) =>
        parent.GetNestedType(Pascal(group) + "Group", BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException($"{parent.Name} has no group {group}");

    private static void SetField(object instance, string pythonName, object? value) =>
        (instance.GetType().GetProperty(Pascal(pythonName), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{instance.GetType().Name} has no field {pythonName}"))
        .SetValue(instance, value);

    private static ChatPrompt Chat(string system, string user) =>
        new() { System = new SystemMessage { Content = system }, User = new UserMessage { Content = user } };

    private static string ContextText(OrderedDictionary<string, object?> context) =>
        Py.Str(context.TryGetValue("k", out var value) ? value : null);

    // The library of markers: each builder of each group names itself and reads `k` of the context.
    private static IPromptLibrary MarkerLibrary()
    {
        OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> overrides = [];
        foreach (var (group, names) in Lib.PromptGroups)
        {
            OrderedDictionary<string, ChatPromptFunction> functions = [];
            foreach (var name in names)
            {
                var full = $"{group}.{name}";
                functions[name] = context => Chat($"LIB {full} {ContextText(context)}", $"LIBU {full}");
            }

            overrides[group] = functions;
        }

        return Lib.CreatePromptLibrary(overrides);
    }

    // A fresh pool of clients, transports, and models for each case, as the recorder builds it.
    private sealed class Pool
    {
        internal List<object?> Events { get; } = [];

        internal Dictionary<string, LlmRuntimeFake> Clients { get; } = [];

        internal Dictionary<string, LLMTransport> Transports { get; } = [];

        internal Dictionary<string, LLMModel> Models { get; } = [];

        internal Dictionary<string, ChatPromptFunction> Markers { get; } = [];

        internal Pool()
        {
            foreach (var name in Items(Document["clients"]))
            {
                Clients[Str(name)] = new LlmRuntimeFake(Str(name), Events);
            }

            foreach (var item in Items(Document["transports"]))
            {
                var transport = Dict(item);
                Transports[Str(transport["name"])] = new LLMTransport(Clients[Str(transport["client"])]);
            }

            foreach (var item in Items(Document["models"]))
            {
                var model = Dict(item);
                Models[Str(model["name"])] = Transports[Str(model["transport"])].Model(
                    Str(model["id"]),
                    model.TryGetValue("overrides", out var overrides) ? BuildOverrides(Dict(overrides)) : null,
                    model.TryGetValue("max_tokens", out var maxTokens) ? Py.AsLong(maxTokens) : null);
            }
        }

        internal ChatPromptFunction Builder(string marker)
        {
            if (!Markers.TryGetValue(marker, out var function))
            {
                function = context =>
                {
                    if (marker == "RAISE")
                    {
                        throw new InvalidOperationException("builder raised");
                    }

                    return Chat($"OVR {marker} {ContextText(context)}", $"OVRU {marker}");
                };
                Markers[marker] = function;
            }

            return function;
        }

        internal LLMPromptOverrides BuildOverrides(OrderedDictionary<string, object?> spec)
        {
            var overrides = new LLMPromptOverrides();
            foreach (var (group, methodsValue) in spec)
            {
                var instance = Activator.CreateInstance(GroupType(typeof(LLMPromptOverrides), group), nonPublic: true)!;
                foreach (var (name, value) in Dict(methodsValue))
                {
                    SetField(instance, name, Builder(Str(value)));
                }

                SetField(overrides, group, instance);
            }

            return overrides;
        }

        internal PromptRoutes BuildRoutes(OrderedDictionary<string, object?> spec)
        {
            var routes = new PromptRoutes();
            foreach (var (group, value) in spec)
            {
                if (value is string modelName)
                {
                    SetField(routes, group, Models[modelName]);
                    continue;
                }

                var instance = Activator.CreateInstance(GroupType(typeof(PromptRoutes), group), nonPublic: true)!;
                foreach (var (name, item) in Dict(value))
                {
                    SetField(instance, name, Models[Str(item)]);
                }

                SetField(routes, group, instance);
            }

            return routes;
        }

        internal LLMRuntime BuildRuntime(OrderedDictionary<string, object?> spec)
        {
            var routes = spec.TryGetValue("routes", out var r) ? BuildRoutes(Dict(r)) : null;
            var overrides = spec.TryGetValue("overrides", out var o) ? BuildOverrides(Dict(o)) : null;
            IPromptLibrary? library = Str(spec["library"]) switch
            {
                "marker" => MarkerLibrary(),
                "default_passed" => Lib.CreatePromptLibrary(),
                _ => null,
            };
            return new LLMRuntime(Models[Str(spec["main"])], routes, overrides, library);
        }

        internal string ModelName(LLMModel model) => Models.Single(entry => ReferenceEquals(entry.Value, model)).Key;

        internal string TransportName(LLMTransport transport) => Transports.Single(entry => ReferenceEquals(entry.Value, transport)).Key;

        internal string MarkerName(ChatPromptFunction function) => Markers.Single(entry => ReferenceEquals(entry.Value, function)).Key;
    }

    // The log lines of the run go to the same list as the calls.
    private sealed class EventFactory(List<object?> events) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new EventLogger(events);

        public void Dispose()
        {
        }

        private sealed class EventLogger(List<object?> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Add(new List<object?> { "log", logLevel.ToString(), formatter(state, exception) });
        }
    }

    // The exception types that carry each Python error class (RULES.md R4).
    private static string PythonErrorType(Exception error) => error switch
    {
        ArgumentException => "ValueError",
        InvalidOperationException => "RuntimeError",
        InvalidCastException => "TypeError",
        MissingMemberException => "AttributeError",
        _ => throw new InvalidOperationException($"No Python class for {error.GetType().Name}: {error.Message}"),
    };

    private static OrderedDictionary<string, object?> Err(Exception error) =>
        new() { ["err"] = new List<object?> { PythonErrorType(error), error.Message } };

    private static async Task<OrderedDictionary<string, object?>> Attempt(Func<Task<object?>> call)
    {
        try
        {
            return new() { ["ok"] = await call() };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Err(error);
        }
    }

    private static OrderedDictionary<string, object?> Attempt(Func<object?> call)
    {
        try
        {
            return new() { ["ok"] = call() };
        }
        catch (Exception error)
        {
            return Err(error);
        }
    }

    private static void AssertSame(object? expected, object? actual, string label) =>
        Assert.True(PyJson.Dumps(expected) == PyJson.Dumps(actual), $"{label}: expected {PyJson.Dumps(expected)} but got {PyJson.Dumps(actual)}");

    // The recorded events hold the keys of the keyword arguments. The port passes the same seven arguments each time.
    private static List<object?> WithoutKeys(object? events)
    {
        List<string> keys = ["attribute_extraction", "group_id", "max_tokens", "model", "model_size", "prompt_name", "response_model"];
        List<object?> result = [];
        foreach (var item in Items(events))
        {
            if (item is List<object?> { Count: 3 } call && call[0] is "call")
            {
                var detail = Dict(call[2]);
                Assert.Equal(keys, Items(detail["keys"]).Select(Str).ToList());
                var copy = new OrderedDictionary<string, object?>(detail);
                copy.Remove("keys");
                result.Add(new List<object?> { call[0], call[1], copy });
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }

    [Fact]
    public void The_prompt_names_of_the_recording_are_the_builtin_prompt_names()
    {
        Assert.Equal(
            Lib.PromptGroups.SelectMany(group => group.Value.Select(name => $"{group.Key}.{name}")).ToList(),
            Items(Document["names"]).Select(Str).ToList());
    }

    [Fact]
    public void The_constructor_gives_each_recorded_transport_order_client_route_and_override()
    {
        var cases = Items(Document["constructor"]);
        Assert.True(cases.Count >= 100);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var spec = Dict(recorded["spec"]);
            var pool = new Pool();
            var runtime = pool.BuildRuntime(spec);
            var actual = new OrderedDictionary<string, object?>
            {
                ["transports"] = runtime.Transports.Select(t => (object?)pool.TransportName(t)).ToList(),
                ["model"] = pool.ModelName(runtime.Model),
                ["client"] = ((LlmRuntimeFake)runtime.Client).Name,
                ["routes"] = runtime.Routes.Select(e => (object?)new List<object?> { e.Key, pool.ModelName(e.Value) }).ToList(),
                ["overrides"] = runtime.PromptOverrides.Select(e => (object?)new List<object?> { e.Key, pool.MarkerName(e.Value) }).ToList(),
            };
            AssertSame(recorded["r"], actual, PyJson.Dumps(spec));
            Assert.Same(pool.Models[Str(spec["main"])].Transport.Client, runtime.Client);
        }
    }

    [Fact]
    public void set_tracer_gives_the_tracer_to_each_client_of_the_transports()
    {
        foreach (var item in Items(Document["constructor"]))
        {
            var recorded = Dict(item);
            var spec = Dict(recorded["spec"]);
            var expected = Dict(recorded["set_tracer"]);
            var pool = new Pool();
            var runtime = pool.BuildRuntime(spec);
            var tracer = new NoOpTracer();
            var before = pool.Clients.ToDictionary(e => e.Key, e => e.Value.Tracer);

            runtime.SetTracer(tracer);

            // Python called each client of the transports one time. The port cannot show the count (SetTracer is not
            // virtual), so the test holds the recorded count against the recorded set, and compares the set.
            var calls = Items(expected["calls"]).Select(Str).ToList();
            Assert.Equal(calls.Count, calls.Distinct().Count());
            foreach (var (name, client) in pool.Clients)
            {
                var reached = ReferenceEquals(client.Tracer, tracer);
                Assert.True((bool)Dict(expected["reached"])[name]! == reached, $"{PyJson.Dumps(spec)}: client {name}");
                Assert.True(reached == calls.Contains(name), name);
                if (!reached)
                {
                    Assert.Same(before[name], client.Tracer);
                }
            }
        }
    }

    [Fact]
    public void resolve_model_and_resolve_builder_give_each_recorded_result_for_each_prompt_name()
    {
        var cases = Items(Document["resolve"]);
        Assert.True(cases.Count >= 20);
        var rows = 0;
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var spec = Dict(recorded["spec"]);
            var pool = new Pool();
            var runtime = pool.BuildRuntime(spec);
            foreach (var rowItem in Items(recorded["rows"]))
            {
                var row = Dict(rowItem);
                var name = Str(row["name"]);
                var model = runtime.ResolveModel(name);
                Assert.True(Str(row["model"]) == pool.ModelName(model), $"{PyJson.Dumps(spec)}: model of {name}");
                var candidates = new Dictionary<string, LLMModel>
                {
                    ["own"] = model,
                    ["m6"] = pool.Models["m6"],
                    ["m7"] = pool.Models["m7"],
                    ["m8"] = pool.Models["m8"],
                };
                var builders = Dict(row["builders"]);
                Assert.Equal(candidates.Keys.ToList(), builders.Keys.ToList());
                foreach (var (label, candidate) in candidates)
                {
                    var actual = Attempt(() =>
                    {
                        var chat = runtime.ResolveBuilder(name, candidate)(new OrderedDictionary<string, object?> { ["k"] = "v" });
                        return $"{chat.System.Content}|{chat.User.Content}";
                    });
                    AssertSame(builders[label], actual, $"{PyJson.Dumps(spec)}: {name} with {label}");
                    rows++;
                }
            }
        }

        Assert.True(rows >= 3000);
    }

    private static OrderedDictionary<string, object?> Context(object? value) => Dict(value);

    private static IResponseModel? ResponseModelOf(OrderedDictionary<string, object?> call)
    {
        if (!call.TryGetValue("response_model", out var value) || value is null)
        {
            return null;
        }

        return Str(value) switch
        {
            "same" => Lib.BuiltinPromptSpecs[Str(call["name"])].ResponseModel,
            "OtherModel" => OtherModel,
            "PersonAttrs" => PersonAttrs,
            var other => throw new InvalidOperationException(other),
        };
    }

    // Each keyword argument that the recorded call names. An argument that the call leaves out takes the default of the
    // C# parameter, so a wrong default shows.
    private static Task<OrderedDictionary<string, object?>> RunCall(LLMRuntime runtime, OrderedDictionary<string, object?> call, CancellationToken cancellationToken)
    {
        var name = Str(call["name"]);
        var context = Context(call["context"]);
        var responseModel = ResponseModelOf(call);
        var method = typeof(LLMRuntime).GetMethod(nameof(LLMRuntime.CompleteAsync), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var arguments = method.GetParameters().Select(p => p.Name switch
        {
            "promptName" => name,
            "context" => context,
            "responseModel" => responseModel,
            "attributeExtraction" => call.TryGetValue("attribute_extraction", out var a) ? (object?)(bool)a! : Type.Missing,
            "groupId" => call.TryGetValue("group_id", out var g) ? g : Type.Missing,
            "maxTokens" => call.TryGetValue("max_tokens", out var m) ? (m is null ? null : (object?)Py.AsLong(m)) : Type.Missing,
            "modelSize" => call.TryGetValue("model_size", out var s) ? (object)(Str(s) == "small" ? ModelSize.Small : ModelSize.Medium) : Type.Missing,
            "cancellationToken" => cancellationToken,
            var other => throw new InvalidOperationException(other),
        }).ToArray();
        return (Task<OrderedDictionary<string, object?>>)method.Invoke(runtime, BindingFlags.OptionalParamBinding, null, arguments, null)!;
    }

    private static async Task<OrderedDictionary<string, object?>> Outcome(LLMRuntime runtime, OrderedDictionary<string, object?> call) =>
        await Attempt(async () => await RunCall(runtime, call, TestContext.Current.CancellationToken));

    [Fact]
    public async Task complete_gives_each_recorded_call_of_the_client_reply_and_error()
    {
        var cases = Items(Document["complete"]);
        Assert.True(cases.Count >= 250);
        var errors = 0;
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var spec = Dict(recorded["spec"]);
            var call = Dict(recorded["call"]);
            var pool = new Pool();
            using var scope = PortServices.Use(loggerFactory: new EventFactory(pool.Events));
            var runtime = pool.BuildRuntime(spec);

            var actual = await Asyncio.Run(() => Outcome(runtime, call));

            var label = $"{PyJson.Dumps(spec)} {PyJson.Dumps(call)}";
            AssertSame(WithoutKeys(recorded["events"]), pool.Events, label);
            AssertSame(recorded["r"], actual, label);
            errors += actual.ContainsKey("err") ? 1 : 0;
        }

        Assert.True(errors >= 60);
    }

    [Fact]
    public async Task calls_that_run_at_the_same_time_give_each_recorded_order_of_calls_and_results()
    {
        var cases = Items(Document["concurrent"]);
        Assert.True(cases.Count >= 50);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var spec = Dict(recorded["spec"]);
            var calls = Items(recorded["calls"]).Select(Dict).ToList();
            var pool = new Pool();
            using var scope = PortServices.Use(loggerFactory: new EventFactory(pool.Events));
            var runtime = pool.BuildRuntime(spec);

            var results = await Asyncio.Run(() => Asyncio.GatherAsync(
                calls.Select(call => (Func<Task<OrderedDictionary<string, object?>>>)(() => Outcome(runtime, call))).ToList()));

            var label = $"{PyJson.Dumps(spec)} {PyJson.Dumps(calls)}";
            AssertSame(WithoutKeys(recorded["events"]), pool.Events, label);
            AssertSame(recorded["r"], results, label);
        }
    }

    // R25: the theory with no Python original. A canceled token reaches the client of the routed model, and the call
    // ends with the cancellation. A call that fails before the client does not see the token.
    [Fact]
    public async Task complete_with_a_canceled_token_ends_with_the_cancellation_for_each_prompt_name()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        foreach (var name in Items(Document["names"]).Select(Str))
        {
            var pool = new Pool();
            var runtime = pool.BuildRuntime(new OrderedDictionary<string, object?> { ["main"] = "m0", ["library"] = "marker" });
            var spec = Lib.BuiltinPromptSpecs[name];
            var responseModel = spec.DynamicSchema ? PersonAttrs : null;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Asyncio.Run(() => runtime.CompleteAsync(name, new() { ["k"] = "v" }, responseModel, cancellationToken: canceled.Token)));

            Assert.Empty(pool.Events);
        }

        var unknown = new Pool().BuildRuntime(new OrderedDictionary<string, object?> { ["main"] = "m0", ["library"] = "marker" });
        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Asyncio.Run(() => unknown.CompleteAsync("bogus.name", new() { ["k"] = "v" }, cancellationToken: canceled.Token)));
        Assert.Equal("Unknown prompt_name: bogus.name", error.Message);
    }

    [Fact]
    public void _wrap_builder_gives_each_recorded_result_context_and_error()
    {
        var cases = Items(Document["wrap_builder"]);
        Assert.True(cases.Count >= 10);
        var skipped = 0;
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            if (recorded["cs"] is false)
            {
                // A builder that returns a list, a text, or None: the delegate type holds a ChatPrompt (R22).
                skipped++;
                continue;
            }

            var name = Str(recorded["name"]);
            List<object?> seen = [];
            ChatPromptFunction inner = Str(recorded["kind"]) switch
            {
                "good" => context =>
                {
                    seen.Add(new OrderedDictionary<string, object?>(context));
                    return Chat("a" + ContextText(context), "ua" + ContextText(context));
                }
                ,
                "raises" => _ => throw new InvalidOperationException("inner"),
                var other => throw new InvalidOperationException(other),
            };

            var wrapped = LlmRuntimeModule.WrapBuilder(inner, name);
            var actual = Attempt(() =>
            {
                var chat = wrapped(recorded.TryGetValue("context", out var context) ? Context(context) : new() { ["k"] = "v" });
                return $"{chat.System.Content}|{chat.User.Content}";
            });

            AssertSame(recorded["r"], actual, PyJson.Dumps(recorded));
            if (recorded.TryGetValue("seen", out var recordedSeen))
            {
                AssertSame(recordedSeen, seen, PyJson.Dumps(recorded));
            }
        }

        Assert.True(skipped >= 8, "The recording holds the cases that have no C# form.");
    }
}
