// Ported from tests/llm_client/test_llm_runtime.py (port/RULES.md, R25): the tests whose code under test is
// prompt_config.py and the parts of llm_runtime.py that do not need the prompt library of layer 4.
//
// The constructor of LLMRuntime ends in Lib.EnsurePromptLibraryWrapped, and `complete` starts with
// Lib.ResolveResponseModel. Both belong to layer 4 and are skeleton members, so each test that builds a runtime with the
// constructor or calls `complete` waits for that layer:
//   test_llm_runtime_routes_to_mapped_model, test_model_prompt_overrides_beat_general_overrides (the call of
//   `complete`), test_schema_override_rejected_on_runtime_complete, test_same_response_model_identity_allowed,
//   test_unmapped_prompt_falls_back_to_default, test_group_route_applies_to_all_prompts_in_group,
//   test_prompt_route_beats_group_route, test_dynamic_schema_requires_response_model,
//   test_dynamic_schema_accepts_call_site_model, test_runtime_never_clones_transport,
//   test_concurrent_completes_do_not_serialize_or_clobber_model, test_runtime_preserves_client_model_configuration,
//   test_runtime_routes_across_transport_clients, test_routed_model_keeps_model_size_without_selecting_small_model,
//   test_runtime_transports_are_unique_and_default_first, test_set_tracer_reaches_each_transport_client (also needs
//   LLMClient.SetTracer of the client unit), and `tests/test_graphiti_configurable_prompts.py` (the Graphiti class of
//   layer 9).
// The routing and the order of the builders are checked below with a runtime that the tests build with no
// constructor call. A test that Python writes as a wrong argument has no C# form, because the parameter types hold the
// right class: test_runtime_rejects_unknown_route_field, test_runtime_rejects_unknown_prompt_override_field,
// test_runtime_rejects_non_prompt_routes_object, test_missing_model_is_a_type_error,
// test_llm_model_rejects_non_callable_override, test_flatten_overrides_rejects_wrong_group_class, and the two
// `object()` arguments of test_transport_and_model_validate_types_and_model_ids. The recording of
// `prompt_config.py` holds those errors, and the judge cases of R22 hold the type rejections.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;

public class LlmClientRuntimeTests
{
    private const string MainId = "gpt-4.1";

    private const string NanoId = "gpt-4.1-nano";

    private static LlmClientPromptConfigFakeLlm FakeLlm() => LlmClientPromptConfigFakeLlm.Create();

    private static (LLMTransport Transport, LLMModel Main, LLMModel Nano) Models(LlmClientPromptConfigFakeLlm client)
    {
        var transport = new LLMTransport(client);
        return (transport, transport.Model(MainId), transport.Model(NanoId));
    }

    private static ChatPromptFunction Chat(string marker) =>
        _ => throw new InvalidOperationException(marker);

    // The constructor of the runtime needs the prompt library of layer 4. This builds the runtime from the same
    // flatten functions that the constructor calls, and leaves the library unset.
    private static LLMRuntime Runtime(LLMModel model, PromptRoutes? routes = null, LLMPromptOverrides? promptOverrides = null)
    {
        var runtime = (LLMRuntime)RuntimeHelpers.GetUninitializedObject(typeof(LLMRuntime));
        runtime.Model = model;
        runtime.Routes = PromptConfig.FlattenRoutes(routes);
        runtime.PromptOverrides = PromptConfig.FlattenOverrides(promptOverrides);
        return runtime;
    }

    [Fact]
    public void test_llm_runtime_routes_to_mapped_model()
    {
        var (_, main, nano) = Models(FakeLlm());
        var runtime = Runtime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } });

        Assert.Same(nano, runtime.ResolveModel("extract_nodes.extract_message"));
        Assert.Equal(MainId, main.Id);
        Assert.Equal(NanoId, runtime.ResolveModel("extract_nodes.extract_message").Id);
    }

    [Fact]
    public void test_model_prompt_overrides_beat_general_overrides()
    {
        var client = FakeLlm();
        var (transport, main, _) = Models(client);
        var nano = transport.Model(
            NanoId,
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = Chat("MODEL") } });
        var runtime = Runtime(
            main,
            new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractMessage = nano } },
            new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = Chat("GENERAL") } });

        // The wrapped builder calls the override first: its marker shows which override the runtime chose.
        var model = runtime.ResolveModel("extract_nodes.extract_message");
        var builder = runtime.ResolveBuilder("extract_nodes.extract_message", model);
        var thrown = Assert.Throws<InvalidOperationException>(() => builder([]));
        Assert.Equal("MODEL", thrown.Message);
    }

    [Fact]
    public void A_general_override_applies_when_the_model_has_none()
    {
        var (_, main, _) = Models(FakeLlm());
        var runtime = Runtime(
            main,
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = Chat("GENERAL") } });

        var builder = runtime.ResolveBuilder("extract_nodes.extract_message", main);
        var thrown = Assert.Throws<InvalidOperationException>(() => builder([]));
        Assert.Equal("GENERAL", thrown.Message);
    }

    [Fact]
    public void test_unmapped_prompt_falls_back_to_default()
    {
        var (_, main, nano) = Models(FakeLlm());
        var runtime = Runtime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { ExtractAttributes = nano } });

        Assert.Equal(MainId, runtime.ResolveModel("extract_nodes.extract_message").Id);
    }

    [Fact]
    public void test_group_route_applies_to_all_prompts_in_group()
    {
        var (_, main, nano) = Models(FakeLlm());
        var runtime = Runtime(main, new PromptRoutes { ExtractNodes = nano });

        Assert.Equal(NanoId, runtime.ResolveModel("extract_nodes.extract_message").Id);
    }

    [Fact]
    public void test_prompt_route_beats_group_route()
    {
        var (transport, main, nano) = Models(FakeLlm());
        var other = transport.Model("gpt-4.1-mini");
        var runtime = Runtime(main, new PromptRoutes { ExtractNodes = new PromptRoutes.ExtractNodesGroup { Default = nano, ExtractMessage = other } });

        Assert.Equal("gpt-4.1-mini", runtime.ResolveModel("extract_nodes.extract_message").Id);
    }

    [Fact]
    public void test_runtime_client_is_the_default_model_client()
    {
        var client = FakeLlm();
        var (_, main, _) = Models(client);
        var runtime = Runtime(main);

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
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = Chat("x") } });
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
            promptOverrides: new LLMPromptOverrides { ExtractNodes = new LLMPromptOverrides.ExtractNodesGroup { ExtractMessage = Chat("x") } }).GetHashCode();
    }
}
