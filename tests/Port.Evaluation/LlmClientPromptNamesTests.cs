// Ported from tests/prompts/test_prompt_names.py (port/RULES.md, R25): the three tests whose code under test is
// prompt_config.py. The other two tests of that file (the literals PromptName and PromptGroup against the registry) are
// ported with the prompt library, in PromptsLibTests.
//
// `fields(cls)` is the set of the declared properties of the record. The tests read the records and not the private
// tables of PromptConfig, so that they do not restate them.
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using System.Text;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;

public class LlmClientPromptNamesTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // The Python name of a C# property: ExtractNodesAndEdges is extract_nodes_and_edges (R2).
    private static string PythonFieldName(string propertyName)
    {
        StringBuilder name = new();
        foreach (var character in propertyName)
        {
            if (char.IsUpper(character) && name.Length > 0)
            {
                name.Append('_');
            }

            name.Append(char.ToLowerInvariant(character));
        }

        return name.ToString();
    }

    // `{item.name for item in fields(cls)}`. The record adds the property EqualityContract, which is no field.
    private static HashSet<string> Fields(Type type) =>
        type.GetProperties(Declared)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => PythonFieldName(property.Name))
            .ToHashSet();

    private static Type Group(Type parent, string groupName)
    {
        var name = string.Concat(groupName.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Group";
        return parent.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{parent.Name} has no group class for {groupName}");
    }

    [Fact]
    public void test_prompt_routes_groups_match_registry()
    {
        Assert.True(Fields(typeof(PromptRoutes)).SetEquals(Lib.PromptGroups.Keys));
    }

    [Fact]
    public void test_prompt_override_groups_match_registry()
    {
        Assert.True(Fields(typeof(LLMPromptOverrides)).SetEquals(Lib.PromptGroups.Keys));
    }

    [Fact]
    public void test_nested_route_and_override_fields_match_prompt_groups()
    {
        var groupClasses = Lib.PromptGroups.Keys.ToDictionary(
            groupName => groupName,
            groupName => (Route: Group(typeof(PromptRoutes), groupName), Override: Group(typeof(LLMPromptOverrides), groupName)));
        Assert.True(groupClasses.Keys.ToHashSet().SetEquals(Lib.PromptGroups.Keys));
        foreach (var (groupName, (routeClass, overrideClass)) in groupClasses)
        {
            var expected = Lib.PromptGroups[groupName].ToHashSet();
            var routeFields = Fields(routeClass).Where(name => name != "default").ToHashSet();
            var overrideFields = Fields(overrideClass);
            Assert.True(routeFields.SetEquals(expected), $"{groupName} routes");
            Assert.True(overrideFields.SetEquals(expected), $"{groupName} overrides");
        }
    }
}
