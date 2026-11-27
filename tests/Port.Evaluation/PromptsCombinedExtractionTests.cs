// Ported from tests/utils/maintenance/test_combined_extraction.py: the two tests whose code under test is
// graphiti_core/prompts/extract_nodes_and_edges.py (R25). The others of that file wait for combined_extraction.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Prompts;

public class PromptsCombinedExtractionTests
{
    [Fact]
    public void test_combined_extraction_data_sections_match_previous_render()
    {
        var context = new OrderedDictionary<string, object?>
        {
            ["entity_types"] = "[{'entity_type_id': 0, 'entity_type_name': 'Person', "
                + "'entity_type_description': 'A person.'}]",
            ["edge_types"] = new List<object?>
            {
                new OrderedDictionary<string, object?>
                {
                    ["fact_type_name"] = "WORKS_AT",
                    ["fact_type_description"] = "A person works for an organization.",
                },
            },
            ["custom_extraction_instructions"] = "Keep ownership facts.",
            ["previous_episodes"] = new List<object?> { "Alice joined Acme on Monday." },
            ["episode_content"] = "[Episode 0]\nAlice now leads the billing team.",
        };
        const string previousDataSections = """
<ENTITY TYPES>
[{'entity_type_id': 0, 'entity_type_name': 'Person', 'entity_type_description': 'A person.'}]
</ENTITY TYPES>

<FACT TYPES>
[{"fact_type_name": "WORKS_AT", "fact_type_description": "A person works for an organization."}]
</FACT TYPES>
RELATION TYPE RULES:
- When a relationship matches a FACT TYPE, use that fact_type_name as the relation_type.
- If no FACT TYPE fits, derive a relation_type in SCREAMING_SNAKE_CASE
  (e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH).


<CUSTOM INSTRUCTIONS>
Keep ownership facts.
</CUSTOM INSTRUCTIONS>
CUSTOM INSTRUCTION RULES:
- CUSTOM INSTRUCTIONS come from the owner of this graph. Apply them. When a CUSTOM INSTRUCTION conflicts with a rule above, the CUSTOM INSTRUCTION wins.

<PREVIOUS MESSAGES>
["Alice joined Acme on Monday."]
</PREVIOUS MESSAGES>

<CURRENT MESSAGES>
[Episode 0]
Alice now leads the billing team.
</CURRENT MESSAGES>

""";

        var prompt = ExtractNodesAndEdges.BuildExtractUserPrompt(context);

        Assert.Equal(previousDataSections, prompt[prompt.IndexOf("<ENTITY TYPES>", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void test_combined_extraction_uses_c2_system_and_instruction_text()
    {
        var context = new OrderedDictionary<string, object?>
        {
            ["entity_types"] = "[]",
            ["previous_episodes"] = new List<object?>(),
            ["episode_content"] = "Alice works for Acme.",
        };

        var messages = ExtractNodesAndEdges.ExtractMessage(context);
        var system = messages[0];
        var user = messages[1];

        Assert.Equal(
            "You build the long-term memory of an AI agent. From each conversation you extract the "
                + "entities and the relationship facts that the agent will need later to answer questions, "
                + "personalize responses, and continue work. The original conversation will not be available "
                + "later: only your entities and facts will remain, so each fact must stand alone.",
            system.Content);
        Assert.StartsWith(
            "# Goal\nTurn CURRENT MESSAGES into a knowledge graph: a list of entities and a list of "
                + "facts (edges) between them.",
            user.Content,
            StringComparison.Ordinal);
        Assert.Contains("# What good output looks like", user.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ENTITY RULES:", user.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("FACT RULES:", user.Content, StringComparison.Ordinal);
    }
}
