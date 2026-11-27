// Evaluation test for custom entity and edge types (debt T18, decision D26, port/RULES.md R7, R15, R25).
// values/custom_types.json holds 23 pydantic classes that port/tools/record_custom_types.py built from type specs, with
// __doc__, the field list that attribute_utils reads, model_json_schema(), and type_model(**data) for many inputs. The
// test builds each type through the public GraphTypeDefinition and compares each value.
namespace Graphiti.Port.Evaluation;

using Graphiti.JudgeHost.Units;
using Graphiti.PyCompat;

public class CustomTypesValuesTests
{
    private static readonly Lazy<List<OrderedDictionary<string, object?>>> Types = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "custom_types.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        return [.. ((List<object?>)document["types"]!).Select(t => (OrderedDictionary<string, object?>)t!)];
    });

    public static TheoryData<string> TypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in Types.Value)
        {
            data.Add((string)Spec(type)["name"]!);
        }

        return data;
    }

    [Fact]
    public void The_recording_covers_each_kind_and_each_option()
    {
        Assert.Equal(23, Types.Value.Count);
        var fields = Types.Value.SelectMany(t => (List<object?>)Spec(t)["fields"]!).Select(f => (OrderedDictionary<string, object?>)f!).ToList();
        foreach (var kind in Enum.GetValues<GraphFieldKind>())
        {
            var ofKind = fields.Where(f => (string)f["kind"]! == kind.ToString()).ToList();
            Assert.Contains(ofKind, f => (bool)f["required"]! && f["description"] is not null);
            Assert.Contains(ofKind, f => (bool)f["required"]! && f["description"] is null);
            Assert.Contains(ofKind, f => !(bool)f["required"]! && f["description"] is not null);
            Assert.Contains(ofKind, f => !(bool)f["required"]! && f["description"] is null);
        }

        Assert.Contains(fields, f => (bool)f["required"]! && f["max_length"] is not null);
        Assert.Contains(fields, f => !(bool)f["required"]! && f["max_length"] is not null);
        Assert.Contains(Types.Value, t => t["doc"] is null);
        Assert.Contains(Types.Value, t => t["doc"] is "");
        var cases = Types.Value.SelectMany(t => (List<object?>)t["cases"]!).Select(c => (OrderedDictionary<string, object?>)c!).ToList();
        Assert.True(cases.Count(c => c.ContainsKey("ok")) > 1000);
        Assert.True(cases.Count(c => c.ContainsKey("err")) > 2000);
    }

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void The_definition_gives_the_docstring_the_fields_and_the_schema_of_python(string name)
    {
        var type = Find(name);
        var spec = Spec(type);
        var definition = Build(spec);
        var descriptor = definition.ToDescriptor();

        Assert.Equal(name, definition.Name);
        Assert.Equal(name, descriptor.PythonName);
        Assert.Equal((string?)type["doc"], definition.Description);
        Assert.Equal((string?)type["doc"], descriptor.Doc);

        var specFields = ((List<object?>)spec["fields"]!).Select(f => (OrderedDictionary<string, object?>)f!).ToList();
        Assert.Equal(specFields.Select(f => (string)f["name"]!), definition.Fields.Select(f => f.Name));
        Assert.Equal(specFields.Select(f => (string)f["kind"]!), definition.Fields.Select(f => f.Kind.ToString()));
        Assert.Equal(specFields.Select(f => (string?)f["description"]), definition.Fields.Select(f => f.Description));
        Assert.Equal(specFields.Select(f => (bool)f["required"]!), definition.Fields.Select(f => f.Required));
        Assert.Equal(specFields.Select(f => (long?)f["max_length"]), definition.Fields.Select(f => f.MaxLength));

        // model_fields as attribute_utils reads it: the keys in order, is_required(), and max_length.
        var modelFields = ((List<object?>)type["model_fields"]!).Select(f => (List<object?>)f!).ToList();
        Assert.Equal(modelFields.Select(f => (string)f[0]!), descriptor.ModelFields.Keys);
        Assert.Equal(modelFields.Select(f => (bool)f[1]!), descriptor.ModelFields.Values.Select(f => f.IsRequired));
        Assert.Equal(modelFields.Select(f => (long?)f[2]), descriptor.ModelFields.Values.Select(f => f.MaxLength));

        // json.dumps(model_json_schema()). PORT: the schema of a field with max_length has no maxLength key
        // (GraphTypeDescriptor.ModelJsonSchema), so a class with such a field compares with the text without the key.
        var expected = (string)(type.GetValueOrDefault("schema_without_max_length") ?? type["schema"])!;
        Assert.Equal(expected, PyJson.Dumps(descriptor.ModelJsonSchema.ToDict()));
    }

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void Validation_gives_the_model_dump_or_the_errors_of_python(string name)
    {
        var type = Find(name);
        var descriptor = Build(Spec(type)).ToDescriptor();
        List<string> failures = [];
        var cases = (List<object?>)type["cases"]!;
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var data = (OrderedDictionary<string, object?>)recorded["data"]!;
            var label = PyJson.Dumps(data);
            string expected;
            if (recorded.TryGetValue("ok", out var ok))
            {
                expected = "ok " + PyJson.Dumps(ok);
            }
            else
            {
                var err = (OrderedDictionary<string, object?>)recorded["err"]!;
                expected = "err " + PyJson.Dumps(err["errors"]) + "\n" + (string)err["text"]!;
            }

            string actual;
            try
            {
                actual = "ok " + PyJson.Dumps(descriptor.ModelValidate((OrderedDictionary<string, object?>)Py.DeepCopy(data)!));
            }
            catch (ValidationException e)
            {
                var errors = e.Errors.Select(error => (object?)new List<object?>
                {
                    error.Loc.Select(part => part is int index ? (object?)(long)index : part).ToList(),
                    error.Type,
                    error.Msg,
                }).ToList();
                actual = "err " + PyJson.Dumps(errors) + "\n" + e.Message;
            }

            if (actual != expected)
            {
                failures.Add($"{label}:\n  expected {expected}\n  got      {actual}");
            }
        }

        Assert.True(cases.Count > 20);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count} differ. First:\n{string.Join("\n", failures.Take(5))}");
    }

    [Theory]
    [InlineData("Person")]
    [InlineData("Product")]
    [InlineData("Company")]
    [InlineData("Likes")]
    [InlineData("WorksAt")]
    public void Each_judge_fixture_equals_the_class_of_fixtures_py(string name)
    {
        var fixture = name switch
        {
            "Person" => FixturesBindings.Person,
            "Product" => FixturesBindings.Product,
            "Company" => FixturesBindings.Company,
            "Likes" => FixturesBindings.Likes,
            _ => FixturesBindings.WorksAt,
        };
        var type = Find(name);

        Assert.Equal(name, fixture.PythonName);
        Assert.Equal((string?)type["doc"], fixture.Doc);
        Assert.Equal((string)type["schema"]!, PyJson.Dumps(fixture.ModelJsonSchema.ToDict()));
        var modelFields = ((List<object?>)type["model_fields"]!).Select(f => (List<object?>)f!).ToList();
        Assert.Equal(modelFields.Select(f => (string)f[0]!), fixture.ModelFields.Keys);
        Assert.Equal(modelFields.Select(f => (bool)f[1]!), fixture.ModelFields.Values.Select(f => f.IsRequired));
        Assert.Equal(modelFields.Select(f => (long?)f[2]), fixture.ModelFields.Values.Select(f => f.MaxLength));
    }

    [Fact]
    public void A_definition_never_changes_and_keeps_one_descriptor()
    {
        var empty = GraphTypeDefinition.Create("Person");
        var one = empty.AddString("occupation", "The job");
        var two = one.AddIntegerList("lucky_numbers", required: true);

        Assert.Empty(empty.Fields);
        Assert.Single(one.Fields);
        Assert.Equal(["occupation", "lucky_numbers"], two.Fields.Select(f => f.Name));
        Assert.Same(two.ToDescriptor(), two.ToDescriptor());
        Assert.Empty(empty.ToDescriptor().ModelFields);
        Assert.Throws<ArgumentException>(() => two.AddBoolean("occupation"));
        Assert.Throws<ArgumentOutOfRangeException>(() => two.AddString("x", maxLength: -1));
        Assert.Throws<ArgumentNullException>(() => GraphTypeDefinition.Create(null!));
        Assert.Throws<ArgumentNullException>(() => empty.AddNumber(null!));
    }

    private static OrderedDictionary<string, object?> Find(string name) => Types.Value.Single(t => (string)Spec(t)["name"]! == name);

    private static OrderedDictionary<string, object?> Spec(OrderedDictionary<string, object?> type) => (OrderedDictionary<string, object?>)type["spec"]!;

    // The public builder call for each field of a spec.
    private static GraphTypeDefinition Build(OrderedDictionary<string, object?> spec)
    {
        var definition = GraphTypeDefinition.Create((string)spec["name"]!, (string?)spec["doc"]);
        foreach (var item in (List<object?>)spec["fields"]!)
        {
            var field = (OrderedDictionary<string, object?>)item!;
            var name = (string)field["name"]!;
            var description = (string?)field["description"];
            var required = (bool)field["required"]!;
            definition = (string)field["kind"]! switch
            {
                "String" => definition.AddString(name, description, required, (long?)field["max_length"]),
                "Integer" => definition.AddInteger(name, description, required),
                "Number" => definition.AddNumber(name, description, required),
                "Boolean" => definition.AddBoolean(name, description, required),
                "StringList" => definition.AddStringList(name, description, required),
                "IntegerList" => definition.AddIntegerList(name, description, required),
                "NumberList" => definition.AddNumberList(name, description, required),
                "BooleanList" => definition.AddBooleanList(name, description, required),
                var kind => throw new InvalidOperationException(kind),
            };
        }

        return definition;
    }
}
