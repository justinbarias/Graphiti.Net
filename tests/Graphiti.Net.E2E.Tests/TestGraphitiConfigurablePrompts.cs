// Port of .upstream/graphiti/tests/test_graphiti_configurable_prompts.py (port/RULES.md, R25): the constructor of the
// Graphiti class and its prompt_library and llm_runtime wiring. No test of the file needs a database.
//
// PORT: `_make_graphiti` builds the class with `MagicMock()` for the driver, the embedder, the cross-encoder, and the model,
// and it replaces `GraphitiClients` with `GraphitiClients.model_construct` so that pydantic does not reject the mocks. In
// C# each argument is a typed fake at the network boundary (StubDriver, MockEmbedder, MockCrossEncoder, MockLlmClient), and
// the record GraphitiClients has no validation, so the patch has no C# form. The fakes make no call in these tests.
namespace Graphiti.E2E.Tests;

using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;

public sealed class TestGraphitiConfigurablePrompts
{
    private static ChatPrompt CustomExtractMessage(OrderedDictionary<string, object?> context) => Prompt("custom system", "custom user");

    private static ChatPrompt Prompt(string system, string user) => new()
    {
        System = new SystemMessage { Content = system }.Validate(),
        User = new UserMessage { Content = user }.Validate(),
    };

    private static IPromptLibrary CustomLibrary(ChatPromptFunction? function = null) =>
        Lib.CreatePromptLibrary(new() { ["extract_nodes"] = new() { ["extract_message"] = function ?? CustomExtractMessage } });

    private static GraphitiCore MakeGraphiti(
        LLMClient? llmClient = null,
        IPromptLibrary? promptLibrary = null,
        LLMRuntime? llmRuntime = null)
    {
        if (llmRuntime is null && llmClient is null)
        {
            llmClient = new MockLlmClient();
        }

        return new GraphitiCore(
            graphDriver: new StubDriver(),
            embedder: new MockEmbedder(),
            crossEncoder: new MockCrossEncoder(),
            llmClient: llmClient,
            promptLibrary: promptLibrary,
            llmRuntime: llmRuntime);
    }

    private static LLMRuntime RuntimeOf(MockLlmClient llm, IPromptLibrary? library = null) =>
        new(model: new LLMTransport(llm).Model("gpt-4.1-mini"), library: library);

    private static string SystemOf(IPromptLibrary? library) =>
        SimpleLibrary.Call(library!, "extract_nodes", "extract_message").System.Content;

    [Fact]
    public void test_graphiti_keeps_prompt_library_unset_when_unconfigured()
    {
        var graphiti = MakeGraphiti();
        Assert.Null(graphiti.PromptLibrary);
        Assert.Null(graphiti.Clients.PromptLibrary);
    }

    [Fact]
    public void test_graphiti_stores_custom_prompt_library()
    {
        var custom = CustomLibrary();
        var graphiti = MakeGraphiti(promptLibrary: custom);
        Assert.Same(custom, graphiti.PromptLibrary);
        Assert.Same(custom, graphiti.Clients.PromptLibrary);
    }

    [Fact]
    public void test_graphiti_clients_include_instance_prompt_library()
    {
        var custom = CustomLibrary();
        var graphiti = MakeGraphiti(promptLibrary: custom);
        Assert.Same(graphiti.PromptLibrary, graphiti.Clients.PromptLibrary);
    }

    [Fact]
    public void test_graphiti_instances_isolate_prompt_libraries()
    {
        var customA = CustomLibrary();

        static ChatPrompt OtherExtract(OrderedDictionary<string, object?> context) => Prompt("other", "other");

        var customB = CustomLibrary(OtherExtract);
        var a = MakeGraphiti(promptLibrary: customA);
        var b = MakeGraphiti(promptLibrary: customB);
        Assert.NotSame(a.PromptLibrary, b.PromptLibrary);
        Assert.NotSame(a.Clients.PromptLibrary, b.Clients.PromptLibrary);
        Assert.Equal("custom system", SystemOf(a.PromptLibrary));
        Assert.Equal("other", SystemOf(b.PromptLibrary));
    }

    [Fact]
    public void test_graphiti_accepts_prompt_library_created_from_overrides()
    {
        var custom = CustomLibrary();
        var graphiti = MakeGraphiti(promptLibrary: custom);
        var prompt = SimpleLibrary.Call(graphiti.PromptLibrary!, "extract_nodes", "extract_message");
        Assert.Equal("custom system", prompt.System.Content);
    }

    [Fact]
    public void test_graphiti_accepts_complete_prompt_library()
    {
        var complete = Lib.CreatePromptLibrary();
        var graphiti = MakeGraphiti(promptLibrary: complete);
        Assert.Same(complete, graphiti.PromptLibrary);
    }

    private static SimpleLibrary HandBuiltCompleteLibrary()
    {
        static ChatPrompt Raw(OrderedDictionary<string, object?> context) => Prompt("hand-built system", "hand-built user");

        return new SimpleLibrary(Lib.PromptGroups.ToDictionary(
            group => group.Key,
            group => (IPromptGroup?)new SimpleGroup(group.Value.ToDictionary(name => name, _ => (ChatPromptFunction?)Raw))));
    }

    [Fact]
    public void test_graphiti_wraps_hand_built_complete_prompt_library()
    {
        var handBuilt = HandBuiltCompleteLibrary();
        var graphiti = MakeGraphiti(promptLibrary: handBuilt);
        var messages = SimpleLibrary.Call(graphiti.PromptLibrary!, "extract_nodes", "extract_message").AsMessages();
        Assert.StartsWith("hand-built system", messages[0].Content, StringComparison.Ordinal);
        Assert.EndsWith(PromptHelpers.DoNotEscapeUnicode, messages[0].Content, StringComparison.Ordinal);
        Assert.False(messages[1].Content.EndsWith(PromptHelpers.DoNotEscapeUnicode, StringComparison.Ordinal));
        Assert.NotNull(graphiti.PromptLibrary!.Specs);
    }

    [Fact]
    public void test_ensure_prompt_library_wrapped_preserves_library_with_specs()
    {
        var custom = CustomLibrary();
        Assert.Same(custom, Lib.EnsurePromptLibraryWrapped(custom));
    }

    [Fact]
    public void test_graphiti_validates_complete_prompt_library()
    {
        var complete = Lib.CreatePromptLibrary();
        Lib.ValidatePromptLibrary(complete);
        MakeGraphiti(promptLibrary: complete);
    }

    [Fact]
    public void test_graphiti_rejects_complete_library_missing_group()
    {
        var incomplete = new SimpleLibrary([]);
        var error = Assert.Throws<ArgumentException>(() => MakeGraphiti(promptLibrary: incomplete));
        Assert.Contains("Prompt library missing group: extract_nodes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_graphiti_rejects_complete_library_missing_function()
    {
        var group = new SimpleGroup([]);
        var incomplete = new SimpleLibrary(Lib.PromptGroups.Keys.ToDictionary(name => name, _ => (IPromptGroup?)group));
        var error = Assert.Throws<ArgumentException>(() => MakeGraphiti(promptLibrary: incomplete));
        Assert.Contains("Prompt library missing function: extract_nodes.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_graphiti_rejects_complete_library_non_callable_function()
    {
        // PORT: the value 'not-callable' is an attribute that is not callable: a null function in the hand-built group.
        var incomplete = new SimpleLibrary(Lib.PromptGroups.ToDictionary(
            group => group.Key,
            group => (IPromptGroup?)new SimpleGroup(group.Value.ToDictionary(name => name, _ => (ChatPromptFunction?)null))));
        var error = Assert.Throws<ArgumentException>(() => MakeGraphiti(promptLibrary: incomplete));
        Assert.Contains("Prompt library function must be callable:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_create_prompt_library_does_not_mutate_default_prompt_library()
    {
        OrderedDictionary<string, object?> context = new()
        {
            ["episode_content"] = "hi",
            ["previous_episodes"] = new List<object?>(),
            ["custom_extraction_instructions"] = string.Empty,
            ["entity_types"] = new List<object?>(),
            ["source_description"] = "test",
        };
        var before = Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractMessage(context).System.Content;
        _ = CustomLibrary();
        var graphiti = MakeGraphiti();
        Assert.Null(graphiti.PromptLibrary);
        Assert.Equal(before, Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractMessage(context).System.Content);
        Assert.NotEqual("custom system", before);
    }

    [Fact]
    public void test_graphiti_rejects_prompt_library_and_llm_runtime_together()
    {
        var custom = CustomLibrary();
        var runtime = RuntimeOf(new MockLlmClient());
        var error = Assert.Throws<ArgumentException>(() => MakeGraphiti(promptLibrary: custom, llmRuntime: runtime));
        Assert.Contains("cannot be combined with: prompt_library", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_graphiti_rejects_llm_client_and_llm_runtime_together()
    {
        var runtime = RuntimeOf(new MockLlmClient());
        var error = Assert.Throws<ArgumentException>(() => MakeGraphiti(llmClient: new MockLlmClient(), llmRuntime: runtime));
        Assert.Contains("cannot be combined with: llm_client", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_graphiti_llm_runtime_populates_prompt_library_from_bundle()
    {
        var llm = new MockLlmClient();
        var custom = CustomLibrary();
        var runtime = RuntimeOf(llm, custom);
        var graphiti = MakeGraphiti(llmRuntime: runtime);
        Assert.Same(runtime, graphiti.LlmRuntime);
        Assert.Same(runtime, graphiti.Clients.LlmRuntime);
        Assert.Same(llm, graphiti.LlmClient);
        Assert.Equal("custom system", SystemOf(graphiti.PromptLibrary));
    }

    [Fact]
    public void test_graphiti_uses_default_runtime_transport_client()
    {
        var llm = new MockLlmClient();
        var runtime = RuntimeOf(llm);

        var graphiti = MakeGraphiti(llmRuntime: runtime);

        Assert.Same(llm, runtime.Client);
        Assert.Same(runtime.Client, graphiti.LlmClient);
    }
}
