// Port of the upstream pytest tests whose code under test is graphiti_core/prompts/lib.py (R25):
//   tests/prompts/test_prompt_exports.py, test_configurable_prompt_library.py, test_prompt_names.py, and the
//   parametrized test of test_legacy_prompt_compatibility.py.
//
// Not ported here, and why:
//   test_prompt_exports.py::test_prompt_library_import_from_prompts_lib and test_prompt_customization_exports_available
//     assert that a name is importable from the package and from the module. `prompts/__init__.py` only imports names
//     again, so it has no C# file (R1), and a name has one place in C#. Nothing is left to compare.
//   test_prompt_names.py: test_prompt_routes_groups_match_registry, test_prompt_override_groups_match_registry, and
//     test_nested_route_and_override_fields_match_prompt_groups wait for graphiti_core/llm_client/prompt_config.py
//     (PromptRoutes, LLMPromptOverrides).
//   test_complete_prompt.py (all 7 tests) waits for graphiti_core/graphiti_types.py (GraphitiClients.complete_prompt,
//     resolve_prompt_messages). Four of them also wait for llm_client/llm_runtime.py and llm_client/prompt_config.py.
//   test_legacy_prompt_compatibility.py: test_legacy_prompt_response_uses_main_default_kwargs,
//     test_legacy_prompt_response_forwards_only_non_default_kwargs,
//     test_legacy_prompt_response_supports_main_llmclient_override_signature,
//     test_legacy_prompt_response_uses_configured_prompt_library, and test_legacy_prompt_response_uses_configured_runtime
//     wait for graphiti_core/graphiti_types.py (generate_prompt_response) and llm_client/client.py. The last one also
//     waits for llm_runtime.py and prompt_config.py. test_node_operation_reads_legacy_prompt_builder_at_call_time waits for
//     utils/maintenance/node_operations.py, and it needs a patch of a method (O10).
//     test_chat_prompt_rejects_invalid_message_shapes and test_chat_prompt_from_messages_copies_content_without_unicode_note
//     test prompts/models.py, and PromptsModelsTests has them.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Prompts;
using static Graphiti.Port.Evaluation.PromptsLibSupport;

public class PromptsLibTests
{
    public static TheoryData<string, string> EveryPromptFunction()
    {
        var data = new TheoryData<string, string>();
        foreach (var (group, functions) in Lib.PromptGroups)
        {
            foreach (var function in functions)
            {
                data.Add(group, function);
            }
        }

        return data;
    }

    // PROMPT_CONTEXT of tests/prompts/test_legacy_prompt_compatibility.py.
    internal static OrderedDictionary<string, object?> PromptContext() => new()
    {
        ["answer"] = "Alice knows Bob.",
        ["attributes"] = new OrderedDictionary<string, object?> { ["role"] = "project lead" },
        ["baseline"] = "Bob is known by Alice.",
        ["candidate"] = "Alice knows Bob.",
        ["custom_extraction_instructions"] = "Use only facts stated in the episode.",
        ["edge_invalidation_candidates"] = "[]",
        ["edge_types"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "KNOWS", ["description"] = "One person knows another." },
        },
        ["entities"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["type"] = "Person" },
        },
        ["entity_summaries"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["summary"] = "Project lead." },
        },
        ["entity_type_description"] = new OrderedDictionary<string, object?> { ["Person"] = "A person mentioned in an episode." },
        ["entity_type_descriptions"] = new OrderedDictionary<string, object?> { ["Person"] = "A person mentioned in an episode." },
        ["entity_types"] = "Person: A person mentioned in an episode.",
        ["episode_content"] = "Alice met Bob at the office on January 2.",
        ["episodes"] = new List<object?> { "Alice met Bob on January 2." },
        ["existing_attributes"] = new OrderedDictionary<string, object?> { ["since"] = "2024" },
        ["existing_edges"] = "[{\"fact\": \"Alice knows Bob.\"}]",
        ["existing_nodes"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["summary"] = "Project lead." },
        },
        ["existing_summary"] = "Alice is preparing a project launch.",
        ["extracted_entities"] = "[{\"name\": \"Alice\", \"type\": \"Person\"}]",
        ["extracted_node"] = new OrderedDictionary<string, object?> { ["name"] = "Alice", ["type"] = "Person" },
        ["extracted_nodes"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["type"] = "Person" },
            new OrderedDictionary<string, object?> { ["name"] = "Bob", ["type"] = "Person" },
        },
        ["fact"] = "Alice knows Bob.",
        ["facts"] = new List<object?> { new OrderedDictionary<string, object?> { ["fact"] = "Alice knows Bob." } },
        ["message"] = "Alice knows Bob.",
        ["new_edge"] = "{\"fact\": \"Alice knows Bob.\"}",
        ["node"] = "Alice",
        ["node_name"] = "Alice",
        ["node_summaries"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["summary"] = "Project lead." },
        },
        ["node_summary"] = "Alice leads the project.",
        ["nodes"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["name"] = "Alice", ["summary"] = "Project lead." },
        },
        ["previous_episodes"] = new List<object?>
        {
            new OrderedDictionary<string, object?> { ["content"] = "Alice met Bob.", ["reference_time"] = "2025-01-02T00:00:00Z" },
        },
        ["previous_messages"] = "User: Who knows Bob?",
        ["query"] = "Who knows Bob?",
        ["reference_time"] = "2025-01-03T00:00:00Z",
        ["response"] = "Alice knows Bob.",
        ["saga_name"] = "Alice's project launch",
        ["source_description"] = "A chat message",
        ["summary"] = "Alice leads the project.",
    };

    // ---- test_prompt_exports.py ----

    [Fact]
    public void test_prompt_library_import_from_prompts_package()
    {
        var messages = Lib.PromptLibrary.ExtractNodes.ExtractMessage(DefaultExtractContext());

        Assert.IsType<Message>(Assert.Single(messages.Take(1)));
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void test_eval_prompts_remain_available_from_default_prompt_library()
    {
        Assert.NotNull(Lib.PromptLibrary.Eval.QueryExpansion);
        Assert.NotNull(Lib.PromptLibrary.Eval.QaPrompt);
        Assert.NotNull(Lib.PromptLibrary.Eval.EvalPromptFunction);
        Assert.NotNull(Lib.PromptLibrary.Eval.EvalAddEpisodeResults);
        Assert.NotNull(((IPromptLibrary)Lib.DefaultChatPromptLibrary).Specs);
    }

    // ---- test_configurable_prompt_library.py ----

    private static OrderedDictionary<string, object?> DefaultExtractContext() => new()
    {
        ["episode_content"] = "hi",
        ["previous_episodes"] = new List<object?>(),
        ["custom_extraction_instructions"] = string.Empty,
        ["entity_types"] = new List<object?>(),
        ["source_description"] = "test",
    };

    private static ChatPromptFunction Function(IPromptLibrary library, string group, string function)
    {
        Assert.True(library.TryGetGroup(group, out var found));
        Assert.NotNull(found);
        Assert.True(found.TryGetFunction(function, out var builder));
        Assert.NotNull(builder);
        return builder;
    }

    private static ChatPromptFunction CustomExtractMessage() => _ => Chat("custom system", "custom user");

    private static OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> ExtractMessageOverride() => new()
    {
        ["extract_nodes"] = new() { ["extract_message"] = CustomExtractMessage() },
    };

    [Fact]
    public void test_default_prompt_library_remains_importable()
    {
        Assert.NotNull(Lib.PromptLibrary.ExtractNodes);
        Assert.NotNull(Lib.DefaultChatPromptLibrary);
        Assert.NotNull(((IPromptLibrary)Lib.DefaultChatPromptLibrary).Specs);
    }

    [Fact]
    public void test_create_prompt_library_returns_default_equivalent_when_no_overrides()
    {
        var library = Lib.CreatePromptLibrary();
        var context = DefaultExtractContext();

        var defaultPrompt = Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractMessage(context);
        var createdPrompt = Function(library, "extract_nodes", "extract_message")(context);

        Assert.Equal(defaultPrompt.System.Content, createdPrompt.System.Content);
        Assert.Equal(defaultPrompt.User.Content, createdPrompt.User.Content);
    }

    [Fact]
    public void test_create_prompt_library_applies_single_override()
    {
        var library = Lib.CreatePromptLibrary(ExtractMessageOverride());

        var prompt = Function(library, "extract_nodes", "extract_message")([]);

        Assert.Equal("custom system", prompt.System.Content);
        Assert.Equal("custom user", prompt.User.Content);
    }

    [Fact]
    public void test_create_prompt_library_preserves_unspecified_defaults()
    {
        var library = Lib.CreatePromptLibrary(ExtractMessageOverride());

        Assert.NotNull(Function(library, "extract_nodes", "extract_text"));
        Assert.NotNull(Function(library, "extract_edges", "edge"));
    }

    [Fact]
    public void test_as_messages_appends_unicode_note_for_overrides()
    {
        var library = Lib.CreatePromptLibrary(ExtractMessageOverride());

        var messages = Function(library, "extract_nodes", "extract_message")([]).AsMessages();

        Assert.EndsWith(PromptHelpers.DoNotEscapeUnicode, messages[0].Content, StringComparison.Ordinal);
        Assert.False(messages[1].Content.EndsWith(PromptHelpers.DoNotEscapeUnicode, StringComparison.Ordinal));
    }

    // The override of the Python test returns list[Message]. A ChatPromptFunction returns a ChatPrompt, so C# cannot
    // hold that override. The check that raises the TypeError is EnsureChatPrompt, and this test reaches it with the list.
    [Fact]
    public void test_list_message_override_raises_type_error()
    {
        var bad = new List<Message> { new Message { Role = "system", Content = "x" }.Validate() };

        var error = Assert.Throws<InvalidCastException>(() => Lib.PrivateEnsureChatPrompt(bad, "override.extract_message"));

        Assert.Contains("ChatPrompt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_unknown_override_group_rejected()
    {
        var overrides = new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>
        {
            ["nope"] = new() { ["extract_message"] = CustomExtractMessage() },
        };

        var error = Assert.Throws<ArgumentException>(() => Lib.CreatePromptLibrary(overrides));

        Assert.Contains("Unknown prompt group", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_unknown_override_function_rejected()
    {
        var overrides = new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>
        {
            ["extract_nodes"] = new() { ["nope"] = CustomExtractMessage() },
        };

        var error = Assert.Throws<ArgumentException>(() => Lib.CreatePromptLibrary(overrides));

        Assert.Contains("Unknown prompt function", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_default_library_exposes_required_groups_and_functions()
    {
        foreach (var (groupName, functions) in Lib.PromptGroups)
        {
            var group = GroupOf(Lib.PromptLibrary, groupName);
            foreach (var functionName in functions)
            {
                Assert.NotNull(FunctionOf(group, groupName, functionName));
            }
        }
    }

    [Fact]
    public void test_prompt_specs_are_fixed()
    {
        var specs = Lib.DefaultChatPromptLibrary.Specs;

        var spec = specs["extract_nodes.extract_message"];

        Assert.Same(ExtractedEntities.ResponseModel, spec.ResponseModel);
        Assert.False(spec.DynamicSchema);
        Assert.True(specs["extract_nodes.extract_attributes"].DynamicSchema);
    }

    [Fact]
    public void test_ensure_prompt_library_wrapped_attaches_specs_to_hand_built_library()
    {
        var handBuilt = HandBuilt(new HashSet<string>(), new HashSet<string>(), new HashSet<(string, string)>(), new HashSet<(string, string)>(), null);

        var wrapped = Lib.EnsurePromptLibraryWrapped(handBuilt);

        Assert.NotNull(wrapped.Specs);
        Assert.True(wrapped.Specs.ContainsKey("extract_nodes.extract_message"));
        var messages = Function(wrapped, "extract_nodes", "extract_message")([]).AsMessages();
        Assert.StartsWith("hand-built system", messages[0].Content, StringComparison.Ordinal);
        Assert.EndsWith(PromptHelpers.DoNotEscapeUnicode, messages[0].Content, StringComparison.Ordinal);
    }

    // ---- test_prompt_names.py ----

    [Fact]
    public void test_prompt_name_literal_matches_builtin_specs()
    {
        Assert.True(Names.PromptName.ToHashSet().SetEquals(Lib.BuiltinPromptSpecs.Keys));
    }

    [Fact]
    public void test_prompt_group_literal_matches_prompt_groups()
    {
        Assert.True(Names.PromptGroup.ToHashSet().SetEquals(Lib.PromptGroups.Keys));
    }

    // ---- test_legacy_prompt_compatibility.py ----

    [Theory]
    [MemberData(nameof(EveryPromptFunction))]
    public void test_chat_prompt_defaults_match_legacy_prompt_text(string groupName, string functionName)
    {
        var legacyBuilder = FunctionOf(GroupOf(Lib.PromptLibrary, groupName), groupName, functionName);
        var chatBuilder = Function(Lib.DefaultChatPromptLibrary, groupName, functionName);
        var context = PromptContext();

        var expected = legacyBuilder(context);
        var actual = chatBuilder(context).AsMessages();

        Assert.Equal(
            expected.Select(m => (m.Role, m.Content)).ToList(),
            actual.Select(m => (m.Role, m.Content)).ToList());
    }
}
