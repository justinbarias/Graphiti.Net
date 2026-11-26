// Evaluation test for graphiti_core/utils/ontology_utils/entity_types_utils.py (port/RULES.md, R23 and R25).
// The upstream tests are in tests/utils/maintenance/test_identity_properties.py. values/entity_types_utils.json holds
// 600 calls that port/tools/record_entity_types_utils_values.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Utils.OntologyUtils;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class EntityTypesUtilsTests
{
    // class Person(BaseModel): email: str
    private static GraphTypeDescriptor Person { get; } =
        new("Person", null, [new GraphFieldDescriptor("email", GraphFieldKind.String, false, null, true, null)]);

    private static OrderedDictionary<string, GraphTypeDescriptor> Types(string name, GraphTypeDescriptor type) =>
        new() { [name] = type };

    private static OrderedDictionary<string, List<string>> Props(string name, params string[] properties) =>
        new() { [name] = [.. properties] };

    [Theory]
    [InlineData("Person", "email", "Company", "entity type is not defined")]
    [InlineData("Person", null, "Person", "non-empty list")]
    [InlineData("Person", "phone", "Person", "not a field")]
    public void test_validate_identity_properties_rejects_invalid_configuration(
        string identityName,
        string? property,
        string typeName,
        string message)
    {
        var props = property is null ? Props(identityName) : Props(identityName, property);
        var error = Assert.Throws<IdentityPropertyValidationException>(
            () => EntityTypesUtils.ValidateIdentityProperties(props, Types(typeName, Person)));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_validate_identity_properties_rejects_invalid_configuration_without_entity_types()
    {
        var error = Assert.Throws<IdentityPropertyValidationException>(
            () => EntityTypesUtils.ValidateIdentityProperties(Props("Person", "email"), null));
        Assert.Contains("entity type is not defined", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_validate_identity_properties_accepts_configured_fields()
    {
        Assert.True(EntityTypesUtils.ValidateIdentityProperties(Props("Person", "email"), Types("Person", Person)));
    }
}
