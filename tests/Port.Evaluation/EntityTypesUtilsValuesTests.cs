// Evaluation test for graphiti_core/utils/ontology_utils/entity_types_utils.py (port/RULES.md, R23 and R25).
// values/entity_types_utils.json holds 600 calls that port/tools/record_entity_types_utils_values.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Utils.OntologyUtils;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class EntityTypesUtilsValuesTests
{
    [Fact]
    public void The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "entity_types_utils.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.Equal(600, cases.Count);

        List<string> failures = [];
        var errors = 0;
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var name = (string)recorded["f"]!;
            var args = (OrderedDictionary<string, object?>)recorded["a"]!;
            var result = (OrderedDictionary<string, object?>)recorded["r"]!;
            var label = $"{name}({PyJson.Dumps(args)})";

            var types = Descriptors(args["types"]);
            string[] actual;
            try
            {
                if (name == "validate_entity_types")
                {
                    Assert.True(EntityTypesUtils.ValidateEntityTypes(types));
                }
                else
                {
                    Assert.True(EntityTypesUtils.ValidateIdentityProperties(Properties(args["props"]), types));
                }

                actual = ["ok"];
            }
            catch (EntityTypeValidationException e)
            {
                actual = ["EntityTypeValidationError", e.Message];
            }
            catch (IdentityPropertyValidationException e)
            {
                actual = ["IdentityPropertyValidationError", e.Message];
            }

            string[] expected;
            if (result.TryGetValue("err", out var err))
            {
                var error = (List<object?>)err!;
                expected = [(string)error[0]!, (string)error[1]!];
                errors++;
            }
            else
            {
                expected = ["ok"];
            }

            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{label}: expected {string.Join(" | ", expected)}, got {string.Join(" | ", actual)}");
            }
        }

        Assert.True(errors > 200);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count} differ. First: {string.Join("\n", failures.Take(5))}");
    }

    private static OrderedDictionary<string, GraphTypeDescriptor>? Descriptors(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new OrderedDictionary<string, GraphTypeDescriptor>(StringComparer.Ordinal);
        foreach (var (typeName, fields) in (OrderedDictionary<string, object?>)value)
        {
            var descriptors = ((List<object?>)fields!)
                .Select(f => new GraphFieldDescriptor((string)f!, GraphFieldKind.String, false, null, true, null));
            result.Add(typeName, new GraphTypeDescriptor("T", null, descriptors));
        }

        return result;
    }

    private static OrderedDictionary<string, List<string>>? Properties(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (typeName, names) in (OrderedDictionary<string, object?>)value)
        {
            result.Add(typeName, ((List<object?>)names!).Select(n => (string)n!).ToList());
        }

        return result;
    }
}
