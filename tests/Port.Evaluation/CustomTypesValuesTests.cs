// Evaluation test for custom entity and edge types (debt T18, decision D26, port/RULES.md R7, R15, R25).
// values/custom_types.json and values/custom_types_2.json hold 44 pydantic classes that port/tools/record_custom_types.py
// built from type specs, with __doc__, the field list that attribute_utils reads, model_json_schema(), and
// type_model(**data) for many inputs. The second file also holds more inputs for the classes of the first file, and the
// field names that pydantic refuses at class creation. The test builds each type through the public GraphTypeDefinition
// and compares each value.
namespace Graphiti.Port.Evaluation;

using Graphiti.JudgeHost.Units;
using Graphiti.PyCompat;

public class CustomTypesValuesTests
{
    private static readonly Lazy<List<OrderedDictionary<string, object?>>> Documents = new(() =>
        [.. new[] { "custom_types.json", "custom_types_2.json" }.Select(file =>
            (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", file)))!)]);

    private static readonly Lazy<List<OrderedDictionary<string, object?>>> Types = new(() =>
        [.. Documents.Value.SelectMany(document => (List<object?>)document["types"]!).Select(t => (OrderedDictionary<string, object?>)t!)]);

    // The inputs of the second file for the classes of the first file, by class name.
    private static readonly Lazy<Dictionary<string, List<object?>>> MoreCases = new(() =>
        ((List<object?>)Documents.Value[1]["more_cases"]!)
            .Select(m => (OrderedDictionary<string, object?>)m!)
            .ToDictionary(m => (string)m["name"]!, m => (List<object?>)m["cases"]!));

    public static TheoryData<string> TypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in Types.Value)
        {
            data.Add((string)Spec(type)["name"]!);
        }

        return data;
    }

    public static TheoryData<string> RefusedNames()
    {
        var data = new TheoryData<string>();
        foreach (var item in (List<object?>)Documents.Value[1]["refused"]!)
        {
            data.Add((string)((OrderedDictionary<string, object?>)item!)["name"]!);
        }

        return data;
    }

    [Fact]
    public void The_recording_covers_each_kind_and_each_option()
    {
        Assert.Equal(44, Types.Value.Count);
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
        Assert.Contains(fields, f => f["description"] is "");
        Assert.Contains(fields, f => ((string)f["name"]!).EndsWith('_'));
        Assert.Contains(fields, f => !((string)f["name"]!).All(char.IsAscii));
        Assert.Contains(Types.Value, t => t["doc"] is null);
        Assert.Contains(Types.Value, t => t["doc"] is "");
        Assert.Contains(Types.Value, t => t["doc"] is string doc && doc.Length > 0 && !t["schema"]!.ToString()!.Contains(PyJson.Dumps(doc), StringComparison.Ordinal));
        Assert.Contains(Types.Value, t => t["schema"]!.ToString()!.Contains("\"maxLength\"", StringComparison.Ordinal));
        var cases = AllCases().Select(c => c.Case).ToList();
        Assert.True(cases.Count(c => c.ContainsKey("ok")) > 3000);
        Assert.True(cases.Count(c => c.ContainsKey("err")) > 6000);
        Assert.True(cases.Count(c => c.ContainsKey("gap")) > 10);
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
        Assert.Equal(specFields.Select(f => (long?)f["max_length"]), definition.Fields.Select(f => (long?)f.MaxLength));

        // model_fields as attribute_utils reads it: the keys in order, is_required(), and max_length.
        var modelFields = ((List<object?>)type["model_fields"]!).Select(f => (List<object?>)f!).ToList();
        Assert.Equal(modelFields.Select(f => (string)f[0]!), descriptor.ModelFields.Keys);
        Assert.Equal(modelFields.Select(f => (bool)f[1]!), descriptor.ModelFields.Values.Select(f => f.IsRequired));
        Assert.Equal(modelFields.Select(f => (long?)f[2]), descriptor.ModelFields.Values.Select(f => f.MaxLength));

        // json.dumps(model_json_schema()).
        Assert.Equal((string)type["schema"]!, PyJson.Dumps(descriptor.ModelJsonSchema.ToDict()));
    }

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void Validation_gives_the_model_dump_or_the_errors_of_python(string name)
    {
        var descriptor = Build(Spec(Find(name))).ToDescriptor();
        var cases = CasesOf(name).Where(c => !c.ContainsKey("gap")).ToList();
        List<string> failures = [];
        foreach (var recorded in cases)
        {
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

        Assert.True(cases.Count > 1);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count} differ. First:\n{string.Join("\n", failures.Take(5))}");
    }

    // GAPS F41: pydantic returns an int that a C# long cannot hold, and the port throws OverflowException (RULES.md R4).
    // The test asserts that each such case still differs, so that a fix shows as a change.
    [Fact]
    public void An_int_that_a_long_cannot_hold_still_throws_OverflowException()
    {
        var gaps = AllCases().Where(c => c.Case.ContainsKey("gap")).ToList();
        Assert.NotEmpty(gaps);
        foreach (var (name, recorded) in gaps)
        {
            Assert.Equal("F41", recorded["gap"]);
            Assert.True(recorded.ContainsKey("ok"));
            var descriptor = Build(Spec(Find(name))).ToDescriptor();
            var data = (OrderedDictionary<string, object?>)Py.DeepCopy(recorded["data"])!;
            Assert.Throws<OverflowException>(() => descriptor.ModelValidate(data));
        }
    }

    // Pydantic raises at class creation for these names (NameError, ValueError, TypeError, or PydanticUserError), or
    // makes a private attribute with no field for `_x: str`. The builder throws ArgumentException at Add (RULES.md R4).
    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void A_field_name_that_pydantic_refuses_throws_ArgumentException(string name)
    {
        var refused = ((List<object?>)Documents.Value[1]["refused"]!).Select(r => (OrderedDictionary<string, object?>)r!).Single(r => (string)r["name"]! == name);
        var forms = ((List<object?>)refused["forms"]!).Select(f => (List<object?>)f!).ToList();
        Assert.Equal(4, forms.Count);
        Assert.All(forms, form => Assert.NotEqual("field", form[2]));
        var empty = GraphTypeDefinition.Create("Refused");
        foreach (var form in forms)
        {
            var required = (bool)form[0]!;
            var description = (bool)form[1]! ? "d" : null;
            foreach (var add in AddMembers(empty))
            {
                var e = Assert.Throws<ArgumentException>(() => add(name, description, required));
                Assert.Equal("name", e.ParamName);
                Assert.Contains(name, e.Message, StringComparison.Ordinal);
            }
        }
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
        Assert.Throws<ArgumentOutOfRangeException>(() => two.AddString("x", maxLength: -1));
    }

    // The two argument checks of the builder throw before the descriptor sees the field, so the type, the parameter
    // name, and the message are the builder's.
    [Fact]
    public void A_null_name_or_a_duplicate_name_throws_the_exception_of_the_builder()
    {
        var two = GraphTypeDefinition.Create("Person").AddString("occupation").AddIntegerList("lucky_numbers");

        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => GraphTypeDefinition.Create(null!)).ParamName);
        foreach (var add in AddMembers(two))
        {
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => add(null!, null, false)).ParamName);
            var duplicate = Assert.Throws<ArgumentException>(() => add("occupation", "d", true));
            Assert.Equal("name", duplicate.ParamName);
            Assert.StartsWith("The type Person has a field occupation already.", duplicate.Message, StringComparison.Ordinal);
        }

        // Names that differ only by case are two fields, as in pydantic (the class CaseNames of the recording).
        Assert.Equal(["a", "A"], GraphTypeDefinition.Create("T").AddInteger("a").AddInteger("A").Fields.Select(f => f.Name));
    }

    // Decision D25: value equality on the name, the description, and the fields in order. The descriptor is not part of it.
    [Fact]
    public void Two_definitions_with_equal_values_are_equal()
    {
        static GraphTypeDefinition Make() => GraphTypeDefinition.Create("Person", "A person.")
            .AddString("occupation", "The job", true, 20)
            .AddBooleanList("flags");

        var a = Make();
        var b = Make();
        Assert.NotSame(a.ToDescriptor(), b.ToDescriptor());
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.Fields[0], b.Fields[0]);
        Assert.Equal(a.Fields[0].GetHashCode(), b.Fields[0].GetHashCode());
        Assert.Equal(GraphTypeDefinition.Create("A"), GraphTypeDefinition.Create("A"));
        Assert.Equal(GraphTypeDefinition.Create("A").GetHashCode(), GraphTypeDefinition.Create("A").GetHashCode());

        GraphTypeDefinition[] others =
        [
            GraphTypeDefinition.Create("Person2", "A person.").AddString("occupation", "The job", true, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person").AddString("occupation", "The job", true, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person").AddString("occupation", "The job", true, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The job", true, 21).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The job", true).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The job", false, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The Job", true, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("Occupation", "The job", true, 20).AddBooleanList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The job", true, 20).AddNumberList("flags"),
            GraphTypeDefinition.Create("Person", "A person.").AddBooleanList("flags").AddString("occupation", "The job", true, 20),
            GraphTypeDefinition.Create("Person", "A person.").AddString("occupation", "The job", true, 20),
        ];
        foreach (var other in others)
        {
            Assert.NotEqual(a, other);
            Assert.False(a == other);
            Assert.NotEqual(a.GetHashCode(), other.GetHashCode());
        }
    }

    private static List<Func<string, string?, bool, GraphTypeDefinition>> AddMembers(GraphTypeDefinition d) =>
    [
        (n, x, r) => d.AddString(n, x, r),
        (n, x, r) => d.AddString(n, x, r, 3),
        (n, x, r) => d.AddInteger(n, x, r),
        (n, x, r) => d.AddNumber(n, x, r),
        (n, x, r) => d.AddBoolean(n, x, r),
        (n, x, r) => d.AddStringList(n, x, r),
        (n, x, r) => d.AddIntegerList(n, x, r),
        (n, x, r) => d.AddNumberList(n, x, r),
        (n, x, r) => d.AddBooleanList(n, x, r),
    ];

    private static OrderedDictionary<string, object?> Find(string name) => Types.Value.Single(t => (string)Spec(t)["name"]! == name);

    private static OrderedDictionary<string, object?> Spec(OrderedDictionary<string, object?> type) => (OrderedDictionary<string, object?>)type["spec"]!;

    private static List<OrderedDictionary<string, object?>> CasesOf(string name) =>
        [.. ((List<object?>)Find(name)["cases"]!).Concat(MoreCases.Value.GetValueOrDefault(name) ?? []).Select(c => (OrderedDictionary<string, object?>)c!)];

    private static IEnumerable<(string Name, OrderedDictionary<string, object?> Case)> AllCases() =>
        Types.Value.Select(t => (string)Spec(t)["name"]!).SelectMany(name => CasesOf(name).Select(c => (name, c)));

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
                "String" => definition.AddString(name, description, required, (int?)(long?)field["max_length"]),
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
