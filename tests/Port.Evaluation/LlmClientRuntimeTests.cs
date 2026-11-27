// Ported from tests/llm_client/test_llm_runtime.py (port/RULES.md, R25): the tests whose code under test is
// prompt_config.py and llm_runtime.py. Each test keeps its Python name and its Python assertions.
//
// The fake client of the upstream file overrides `generate_response` and `set_tracer`. `LLMClient.SetTracer` is not
// virtual, so test_set_tracer_reaches_each_transport_client reads the tracer of each client: it compares which clients
// hold the tracer, and the count of calls (`== [tracer]`) has no C# form. `assert 'small_model' not in call` and
// `assert not hasattr(runtime, '_clients')` have no C# form either: the call has no such parameter, and a C# class
// has no attribute lookup by name. The fake yields first and then records its call (R25).
//
// A test that Python writes with a wrong argument has no C# form, because the parameter types hold the right class:
// test_runtime_rejects_unknown_route_field, test_runtime_rejects_unknown_prompt_override_field,
// test_runtime_rejects_non_prompt_routes_object, test_missing_model_is_a_type_error,
// test_llm_model_rejects_non_callable_override, test_flatten_overrides_rejects_wrong_group_class, and the two
// `object()` arguments of test_transport_and_model_validate_types_and_model_ids. The recording of
// `prompt_config.py` holds those errors, and the judge cases of R22 hold the type rejections.
//
// Tests of other files that build an LLMRuntime and wait for other units:
// - test_runtime_routes_only_dedupe_prompts_to_jev and test_jev_prompt_overrides_build_n6_and_e2_questions
//   (test_jev_client.py) need the unit graphiti_core.llm_client.jev_client.
// - test_shared_prompt_renders_match_zep_fixture_for_both_clients (test_openai_decisions_client.py) is named in
//   LlmClientDecisionClientTests.
// - tests/test_graphiti_configurable_prompts.py needs the Graphiti class of layer 9. The code under test of
//   tests/prompts/test_complete_prompt.py and tests/prompts/test_legacy_prompt_compatibility.py is the prompt library.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;

// The FakeLLM of the upstream test: it records the pinned model id and the arguments of each call.
internal sealed class LlmRuntimeTestFakeLlm : LLMClient
{
    internal LlmRuntimeTestFakeLlm(string model = "gpt-4.1")
        : base(new LLMConfig(model: model), false)
    {
    }

    internal List<RecordedCall> Calls { get; } = [];

    internal override string PythonName => "FakeLLM";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderedDictionary<string, object?> { ["extracted_entities"] = new List<object?>() });

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
        Calls.Add(new RecordedCall(messages, Model, this, responseModel, maxTokens, modelSize, groupId, promptName, attributeExtraction, model));
        return new OrderedDictionary<string, object?> { ["extracted_entities"] = new List<object?>() };
    }

    internal sealed record RecordedCall(
        List<Message> Messages,
        string? TransportModel,
        LLMClient Client,
        IResponseModel? ResponseModel,
        long? MaxTokens,
        ModelSize ModelSize,
        string? GroupId,
        string? PromptName,
        bool AttributeExtraction,
        string? Model);
}

public class LlmClientRuntimeTests
{
    private const string MainId = "gpt-4.1";

    private const string NanoId = "gpt-4.1-nano";

    private static readonly IResponseModel Other = new BuiltinResponseModel("Other", EntitySummary.ModelJsonSchema);

    private static readonly IResponseModel PersonAttrs = new BuiltinResponseModel("PersonAttrs", EntitySummary.ModelJsonSchema);

    private static LlmRuntimeTestFakeLlm FakeLlm(string model = MainId) => new(model);

    private static (LLMTransport Transport, LLMModel Main, LLMModel Nano) Models(LlmRuntimeTestFakeLlm client)
    {
        var transport = new LLMTransport(client);
        return (transport, transport.Model(MainId), transport.Model(NanoId));
    }

    private static ChatPrompt Chat(string marker) =>
        new() { System = new SystemMessage { Content = marker }, User = new UserMessage { Content = marker } };

    private static OrderedDictionary<string, object?> ExtractCtx() => new()
    {
        ["episode_content"] = "hi",
        ["previous_episodes"] = new List<object?>(),
        ["custom_extraction_instructions"] = string.Empty,
        ["entity_types"] = new List<object?>(),
        ["source_description"] = "t",
    };

    private static OrderedDictionary<string, object?> EdgeCtx() => new()
    {
        ["existing_edges"] = new List<object?>(),
        ["edge_invalidation_candidates"] = new List<object?>(),
        ["new_edge"] = "x",
    };

    private static Task<OrderedDictionary<string, object?>> Complete(
        LLMRuntime runtime,
        string promptName,
        OrderedDictionary<string, object?> context,
        IResponseModel? responseModel = null,
        bool attributeExtraction = false,
        ModelSize modelSize = ModelSize.Medium) =>
        Asyncio.Run(() => runtime.CompleteAsync(
            promptName,
            context,
            responseModel,
            attributeExtraction,
            modelSize: modelSize,
            cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public async Task test_llm_runtime_routes_to_mapped_model()
    {
        var client = FakeLlm();
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        Assert.Equal("gpt-4.1", client.Model);
        Assert.Single(client.Calls);
        Assert.Equal("gpt-4.1-nano", client.Calls[0].Model);
        Assert.Equal("extract_nodes.extract_message", client.Calls[0].PromptName);
        Assert.Same(ExtractedEntities.ResponseModel, client.Calls[0].ResponseModel);
    }

    [Fact]
    public async Task test_model_prompt_overrides_beat_general_overrides()
    {
        var client = FakeLlm();
        var (transport, main, _) = Models(client);
        var nano = transport.Model(
            NanoId,
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = _ => Chat("MODEL") } });
        var runtime = new LLMRuntime(
            main,
            new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } },
            new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = _ => Chat("GENERAL") } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        var messages = client.Calls[0].Messages;
        Assert.StartsWith("MODEL", messages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task test_schema_override_rejected_on_runtime_complete()
    {
        var (_, main, _) = Models(FakeLlm());
        var runtime = new LLMRuntime(main);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Complete(runtime, "extract_nodes.extract_message", ExtractCtx(), Other));

        Assert.Contains("schema overrides are not allowed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task test_same_response_model_identity_allowed()
    {
        var client = FakeLlm();
        var (_, main, _) = Models(client);
        var runtime = new LLMRuntime(main);

        await Complete(runtime, "dedupe_edges.resolve_edge", EdgeCtx());

        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task test_unmapped_prompt_falls_back_to_default()
    {
        var client = FakeLlm();
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractAttributes = nano } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        Assert.Equal("gpt-4.1", client.Calls[0].Model);
    }

    [Fact]
    public async Task test_group_route_applies_to_all_prompts_in_group()
    {
        var client = FakeLlm();
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = nano });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        Assert.Equal("gpt-4.1-nano", client.Calls[0].Model);
    }

    [Fact]
    public async Task test_prompt_route_beats_group_route()
    {
        var client = FakeLlm();
        var (transport, main, nano) = Models(client);
        var other = transport.Model("gpt-4.1-mini");
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { Default = nano, ExtractMessage = other } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        Assert.Equal("gpt-4.1-mini", client.Calls[0].Model);
    }

    [Fact]
    public async Task test_dynamic_schema_requires_response_model()
    {
        var (_, main, _) = Models(FakeLlm());
        var runtime = new LLMRuntime(main);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Complete(runtime, "extract_nodes.extract_attributes", new() { ["x"] = 1L }));

        Assert.Contains("dynamic_schema", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task test_dynamic_schema_accepts_call_site_model()
    {
        var client = FakeLlm();
        var (_, main, _) = Models(client);
        var runtime = new LLMRuntime(main);

        await Complete(
            runtime,
            "extract_nodes.extract_attributes",
            new()
            {
                ["node"] = new OrderedDictionary<string, object?>
                {
                    ["name"] = "Alice",
                    ["entity_types"] = new List<object?> { "Entity" },
                    ["attributes"] = new OrderedDictionary<string, object?>(),
                },
                ["episode_content"] = "hi",
                ["previous_episodes"] = new List<object?>(),
            },
            PersonAttrs,
            attributeExtraction: true);

        var kwargs = client.Calls[0];
        Assert.Same(PersonAttrs, kwargs.ResponseModel);
        Assert.True(kwargs.AttributeExtraction);
    }

    [Fact]
    public async Task test_runtime_never_clones_transport()
    {
        var client = FakeLlm();
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());
        await Complete(runtime, "dedupe_edges.resolve_edge", EdgeCtx());

        Assert.Equal(2, client.Calls.Count);
        Assert.All(client.Calls, call => Assert.Same(client, call.Client));
    }

    [Fact]
    public async Task test_concurrent_completes_do_not_serialize_or_clobber_model()
    {
        var client = FakeLlm();
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } });

        await Asyncio.Run(() => Asyncio.GatherAsync(
            [
                () => runtime.CompleteAsync("extract_nodes.extract_message", ExtractCtx(), cancellationToken: TestContext.Current.CancellationToken),
                () => runtime.CompleteAsync("dedupe_edges.resolve_edge", EdgeCtx(), cancellationToken: TestContext.Current.CancellationToken),
            ]));

        var models = client.Calls.Select(call => call.Model).ToHashSet();
        Assert.True(models.SetEquals(["gpt-4.1", "gpt-4.1-nano"]));
        Assert.Equal("gpt-4.1", client.Model);
    }

    [Fact]
    public async Task test_runtime_preserves_client_model_configuration()
    {
        var client = FakeLlm(MainId);
        client.SmallModel = "kept-small";
        var (_, main, nano) = Models(client);
        var runtime = new LLMRuntime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } });

        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());
        await Complete(runtime, "dedupe_edges.resolve_edge", EdgeCtx());

        Assert.Equal(MainId, client.Model);
        Assert.Equal("kept-small", client.SmallModel);
    }

    [Fact]
    public async Task test_runtime_routes_across_transport_clients()
    {
        var clientA = FakeLlm();
        var clientB = FakeLlm();
        var (transportA, _, _) = Models(clientA);
        var transportB = new LLMTransport(clientB);
        var runtime = new LLMRuntime(
            transportA.Model("a-id"),
            new PromptRoutes { DedupeEdges = transportB.Model("b-id") });

        await Complete(runtime, "dedupe_edges.resolve_edge", EdgeCtx());
        await Complete(runtime, "extract_nodes.extract_message", ExtractCtx());

        Assert.Equal(["b-id"], clientB.Calls.Select(call => call.Model).ToList());
        Assert.Equal(["a-id"], clientA.Calls.Select(call => call.Model).ToList());
    }

    [Fact]
    public async Task test_routed_model_keeps_model_size_without_selecting_small_model()
    {
        var clientA = FakeLlm();
        var clientB = FakeLlm();
        var (_, main, _) = Models(clientA);
        var transportB = new LLMTransport(clientB);
        var runtime = new LLMRuntime(main, new PromptRoutes { DedupeEdges = transportB.Model("routed-id") });

        await Complete(runtime, "dedupe_edges.resolve_edge", EdgeCtx(), modelSize: ModelSize.Small);

        Assert.Equal("routed-id", clientB.Calls[0].Model);
        Assert.Equal(ModelSize.Small, clientB.Calls[0].ModelSize);
    }

    [Fact]
    public void test_runtime_transports_are_unique_and_default_first()
    {
        var clientA = FakeLlm();
        var clientB = FakeLlm();
        var (transportA, _, _) = Models(clientA);
        var transportB = new LLMTransport(clientB);
        var runtime = new LLMRuntime(
            transportA.Model("default"),
            new PromptRoutes
            {
                ExtractNodes = transportA.Model("extract"),
                DedupeEdges = transportB.Model("dedupe"),
                ExtractEdges = transportA.Model("edges"),
            });

        Assert.Equal(2, runtime.Transports.Count);
        Assert.Same(transportA, runtime.Transports[0]);
        Assert.Same(transportB, runtime.Transports[1]);
    }

    [Fact]
    public void test_set_tracer_reaches_each_transport_client()
    {
        var clientA = FakeLlm();
        var clientB = FakeLlm();
        var (_, main, _) = Models(clientA);
        var transportB = new LLMTransport(clientB);
        var runtime = new LLMRuntime(main, new PromptRoutes { DedupeEdges = transportB.Model("b-id") });
        var tracer = new NoOpTracer();
        Assert.NotSame(tracer, clientA.Tracer);
        Assert.NotSame(tracer, clientB.Tracer);

        runtime.SetTracer(tracer);

        Assert.Same(tracer, clientA.Tracer);
        Assert.Same(tracer, clientB.Tracer);
    }

    [Fact]
    public void A_general_override_applies_when_the_model_has_none()
    {
        var (_, main, _) = Models(FakeLlm());
        var runtime = new LLMRuntime(
            main,
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = _ => Chat("GENERAL") } });

        var chat = runtime.ResolveBuilder("extract_nodes.extract_message", main)(ExtractCtx());

        Assert.Equal("GENERAL", chat.System.Content);
    }

    [Fact]
    public void test_runtime_client_is_the_default_model_client()
    {
        var client = FakeLlm();
        var (_, main, _) = Models(client);
        var runtime = new LLMRuntime(main);

        Assert.Same(main.Transport.Client, runtime.Client);
        Assert.Same(client, runtime.Client);
    }

    [Fact]
    public void test_transport_rejects_undeclared_model_ids()
    {
        var transport = new LLMTransport(FakeLlm(), ["a"]);
        var first = Assert.Throws<ArgumentException>(() => transport.Model("x"));
        Assert.Contains("'x' is not declared", first.Message, StringComparison.Ordinal);
        var second = Assert.Throws<ArgumentException>(() => new LLMModel { Id = "x", Transport = transport }.Validate());
        Assert.Contains("'x' is not declared", second.Message, StringComparison.Ordinal);

        var unrestricted = new LLMTransport(FakeLlm());
        Assert.Equal("any-id", unrestricted.Model("any-id").Id);
    }

    [Fact]
    public void test_transport_and_model_validate_types_and_model_ids()
    {
        // The two `object()` arguments of the Python test have no C# form: the parameters hold an LLMTransport and an LLMClient.
        foreach (var models in new[] { Array.Empty<string>(), [string.Empty], ["  "] })
        {
            var error = Assert.Throws<ArgumentException>(() => new LLMTransport(FakeLlm(), models));
            Assert.Contains("non-empty model ids", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void test_models_compare_and_hash_by_transport_identity()
    {
        var client = FakeLlm();
        var transportA = new LLMTransport(client);
        var transportB = new LLMTransport(client);
        var first = transportA.Model("same-id");
        var sameTransport = transportA.Model("same-id");
        var overrideModel = transportA.Model(
            "same-id",
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = _ => Chat("x") } });
        var otherTransport = transportB.Model("same-id");

        Assert.Equal(first, sameTransport);
        Assert.Equal(first.GetHashCode(), sameTransport.GetHashCode());
        Assert.Equal(first, overrideModel);
        Assert.Equal(first.GetHashCode(), overrideModel.GetHashCode());
        Assert.NotEqual(first, otherTransport);
    }

    [Fact]
    public void test_llm_model_rejects_empty_id()
    {
        var transport = new LLMTransport(FakeLlm());
        var error = Assert.Throws<ArgumentException>(() => transport.Model("  "));
        Assert.Contains("non-empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_llm_model_rejects_non_positive_max_tokens()
    {
        var transport = new LLMTransport(FakeLlm());
        var error = Assert.Throws<ArgumentException>(() => transport.Model(MainId, maxTokens: 0));
        Assert.Contains("positive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_llm_model_is_hashable()
    {
        var transport = new LLMTransport(FakeLlm());
        _ = transport.Model(MainId).GetHashCode();
        _ = transport.Model(
            MainId,
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = _ => Chat("x") } }).GetHashCode();
    }
}
