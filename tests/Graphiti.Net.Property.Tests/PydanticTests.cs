namespace Graphiti.PyCompat.Tests.Models;

using DecoderFallbackException = System.Text.DecoderFallbackException;
using System.Text.Json;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.Models.PydanticValues;

public class PydanticTests
{
    /// <summary>The reader of each recorded field type, and the constraint that <c>Validate()</c> checks after it.</summary>
    private static readonly Dictionary<string, (Func<object?, object?> Read, Action<PydanticFields, object?>? Check)> Readers = new()
    {
        ["str"] = (v => Pydantic.Str(v), null),
        ["int"] = (v => Pydantic.Int(v), null),
        ["float"] = (v => Pydantic.Float(v), null),
        ["bool"] = (v => Pydantic.Bool(v), null),
        ["datetime"] = (v => Pydantic.DateTime(v), null),
        ["any"] = (Pydantic.Any, null),
        ["enum"] = (v => Pydantic.Enum<ComparisonOperator>(v), null),
        ["enum_episode_type"] = (v => Pydantic.Enum<EpisodeType>(v), null),
        ["literal"] = (v => Pydantic.Literal(v, "system"), null),
        ["str_int_float_none"] = (v => Pydantic.Optional(v, Pydantic.StrIntFloat), null),
        ["optional_str"] = (v => Pydantic.Optional(v, Pydantic.Str), null),
        ["optional_int"] = (v => Pydantic.OptionalValue(v, Pydantic.Int), null),
        ["optional_float"] = (v => Pydantic.OptionalValue(v, Pydantic.Float), null),
        ["optional_bool"] = (v => Pydantic.OptionalValue(v, Pydantic.Bool), null),
        ["optional_datetime"] = (v => Pydantic.OptionalValue(v, Pydantic.DateTime), null),
        ["list_str"] = (v => Pydantic.List(v, Pydantic.Str), null),
        ["list_int"] = (v => Pydantic.List(v, Pydantic.Int), null),
        ["list_float"] = (v => Pydantic.List(v, Pydantic.Float), null),
        ["list_bool"] = (v => Pydantic.List(v, Pydantic.Bool), null),
        ["optional_list_str"] = (v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Str)), null),
        ["list_enum"] = (v => Pydantic.List(v, Pydantic.Enum<EdgeSearchMethod>), null),
        ["dict_any"] = (v => Pydantic.Dict(v, Pydantic.Any), null),
        ["optional_dict_any"] = (v => Pydantic.Optional(v, x => Pydantic.Dict(x, Pydantic.Any)), null),
        ["dict_str"] = (v => Pydantic.Dict(v, Pydantic.Str), null),
        ["dict_float"] = (v => Pydantic.Dict(v, Pydantic.Float), null),
        ["dict_int"] = (v => Pydantic.Dict(v, Pydantic.Int), null),
        ["dict_list_str"] = (v => Pydantic.Dict(v, x => Pydantic.List(x, Pydantic.Str)), null),
        ["callable"] = (v => Pydantic.Optional(v, Pydantic.Callable<Func<string, bool>>), null),
        ["ge_int"] = (v => Pydantic.Int(v), (f, x) => f.Ge("x", (long)(x ?? 0L), 1)),
        ["gt_float"] = (v => Pydantic.Float(v), (f, x) => f.Gt("x", (double)(x ?? 0.0), 0)),
        ["ge_float"] = (v => Pydantic.Float(v), (f, x) => f.Ge("x", (double)(x ?? 0.0), 0)),
        ["max_length"] = (v => Pydantic.Optional(v, Pydantic.Str), (f, x) => f.MaxLength("x", (string?)x, 5)),
    };

    public static TheoryData<string> FieldTypes => [.. Readers.Keys];

    [Fact]
    public void Each_recorded_field_type_has_a_reader_and_enough_cases()
    {
        var recorded = Root.GetProperty("cases").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(recorded.Order(StringComparer.Ordinal), Readers.Keys.Order(StringComparer.Ordinal));
        // A T | None kind has a few cases. The plain kind T has the full set.
        foreach (var name in recorded.Where(n => n is not ("callable" or "ge_int" or "gt_float" or "ge_float" or "max_length" or "dict_int")))
        {
            Assert.True(Cases(name).Count >= (name.StartsWith("optional_", StringComparison.Ordinal) ? 19 : 60), $"{name}: {Cases(name).Count} cases");
        }
    }

    [Theory]
    [MemberData(nameof(FieldTypes))]
    public void Field_reader_matches_pydantic(string name)
    {
        var (read, check) = Readers[name];
        ForEach(Cases(name), c =>
        {
            var input = new Decoder().Decode(c.GetProperty("v"));
            var context = $"{name} of {c.GetProperty("v")}";
            object? Run()
            {
                var f = new PydanticFields("Model", new OrderedDictionary<string, object?> { ["x"] = input });
                var x = f.Required("x", read);
                f.ThrowIfAny();
                var g = new PydanticFields("Model");
                check?.Invoke(g, x.Value);
                g.ThrowIfAny();
                return x.Value;
            }

            if (c.TryGetProperty("big", out var big))
            {
                var ex = Record.Exception(Run);
                Assert.True(ex is OverflowException, $"{context}: expected OverflowException (Python gave {big}), got {ex?.GetType().Name ?? "no exception"}");
            }
            else if (name is "int" or "optional_int" && input is Enum && c.TryGetProperty("r", out var r) && r.TryGetProperty("s", out _))
            {
                // pydantic returns the text value of an enum member for an int field. A long cannot hold it.
                Assert.IsType<InvalidCastException>(Record.Exception(Run));
            }
            else if (c.TryGetProperty("errors", out var errors))
            {
                var ex = Record.Exception(Run);
                var vex = ex as ValidationException ?? throw new Xunit.Sdk.XunitException($"{context}: expected ValidationException, got {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
                AssertErrors(errors, vex, context);
                if (c.TryGetProperty("text", out var text))
                {
                    Assert.Equal(Text(text), vex.Message);
                }

                Assert.Equal("Model", vex.Title);
            }
            else
            {
                object? actual = null;
                var ex = Record.Exception(() => actual = Run());
                Assert.True(ex is null, $"{context}: expected a value, got {ex?.GetType().Name}: {ex?.Message}");
                AssertValue(new Decoder().Decode(c.GetProperty("r")), actual, context);
            }
        });
    }

    [Fact]
    public void ModelValidate_and_ModelDump_match_pydantic_for_upstream_models()
    {
        // pydantic runs a field validator and a constraint with the type checks of the other fields. R7 puts them in
        // Validate(), which runs after the field reads. So when a field has a type error, the port reports the type
        // errors only, and the error list is shorter. The exception type is the same.
        string[] validateTypes = ["value_error", "greater_than", "greater_than_equal"];
        var cases = List("models");
        Assert.True(cases.Count >= 60, $"{cases.Count} model cases");
        Assert.True(cases.Select(c => c.GetProperty("model").GetString()).Distinct().Count() >= 8);
        ForEach(cases, c =>
        {
            var model = c.GetProperty("model").GetString() ?? string.Empty;
            var context = $"{model} of {c.GetProperty("data")}";
            var data = new Decoder().DecodeDict(c.GetProperty("data"));
            if (c.TryGetProperty("big", out _))
            {
                Assert.IsType<OverflowException>(Record.Exception(() => { TestModels.Validate(model, data); }));
            }
            else if (c.TryGetProperty("errors", out var errors))
            {
                var ex = Assert.IsType<ValidationException>(Record.Exception(() => { TestModels.Validate(model, data); }), exactMatch: true);
                var types = errors.EnumerateArray().Select(e => e[1].GetString() ?? string.Empty).ToList();
                var mixed = types.Any(t => validateTypes.Contains(t)) && types.Any(t => !validateTypes.Contains(t));
                if (mixed)
                {
                    var expected = errors.EnumerateArray().Where(e => !validateTypes.Contains(e[1].GetString())).Select(ErrorText);
                    Assert.Equal(expected, ex.Errors.Select(e => ErrorText(ToJson(e))));
                }
                else
                {
                    AssertErrors(errors, ex, context);
                    Assert.Equal(Text(c.GetProperty("text")), ex.Message);
                }

                Assert.Equal(model, ex.Title);
            }
            else
            {
                var m = TestModels.Validate(model, data);
                var dump = new Decoder().DecodeDict(c.GetProperty("dump"));
                var json = new Decoder().DecodeDict(c.GetProperty("json"));
                if (model == "SearchConfig" && !data.ContainsKey("reranker_min_score"))
                {
                    // The one float field with an int default, Field(default=0). pydantic does not validate a default,
                    // so Python dumps the int 0. The port's double property holds 0.0.
                    Assert.Equal(0L, dump["reranker_min_score"]);
                    dump["reranker_min_score"] = json["reranker_min_score"] = 0.0;
                }

                AssertValue(dump, m.ModelDump(), context);
                AssertValue(json, m.ModelDump(json: true), context + " (json)");
            }
        });
    }

    [Fact]
    public void An_int_that_a_long_cannot_hold_gives_the_errors_of_the_other_fields()
    {
        // pydantic accepts the big int. The port throws OverflowException for it only if no other field has an error.
        var cases = List("two_fields");
        Assert.Equal(6, cases.Count);
        ForEach(cases, c =>
        {
            var context = c.GetProperty("data").ToString();
            OrderedDictionary<string, object?> Run()
            {
                var f = new PydanticFields("Model", new Decoder().DecodeDict(c.GetProperty("data")));
                var x = f.Required("x", Pydantic.Int);
                var y = f.Required("y", Pydantic.Int);
                f.ThrowIfAny();
                return new() { ["x"] = x.Value, ["y"] = y.Value };
            }

            if (c.TryGetProperty("big", out _))
            {
                Assert.IsType<OverflowException>(Record.Exception(() => Run()), exactMatch: true);
            }
            else if (c.TryGetProperty("errors", out var errors))
            {
                AssertErrors(errors, Assert.IsType<ValidationException>(Record.Exception(() => Run()), exactMatch: true), context);
            }
            else
            {
                AssertValue(new Decoder().Decode(c.GetProperty("r")), Run(), context);
            }
        });
    }

    [Fact]
    public void ModelFields_match_model_fields()
    {
        foreach (var model in Root.GetProperty("fields").EnumerateObject())
        {
            var expected = model.Value.EnumerateArray().Select(f => (f[0].GetString() ?? string.Empty, f[1].GetBoolean())).ToList();
            var actual = TestModels.Fields[model.Name].Select(kv => (kv.Key, kv.Value.IsRequired)).ToList();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Wrap_turns_a_validator_ValueError_into_ValidationException()
    {
        foreach (var c in List("wrap"))
        {
            var model = c.GetProperty("model").GetString() ?? string.Empty;
            var data = new Decoder().DecodeDict(c.GetProperty("data"));
            var ex = Assert.IsType<ValidationException>(Record.Exception(() => { TestModels.Validate(model, data); }), exactMatch: true);
            AssertErrors(c.GetProperty("errors"), ex, model);
            Assert.Equal(Text(c.GetProperty("text")), ex.Message);
            Assert.Equal(Text(c.GetProperty("inner")), ex.InnerException?.Message);
            Assert.IsType(model == "SearchFilters" ? typeof(NodeLabelValidationException) : typeof(ArgumentException), ex.InnerException, exactMatch: true);
        }
    }

    [Fact]
    public void Wrap_lets_other_exceptions_through()
    {
        // pydantic converts only ValueError (and AssertionError, which upstream validators do not raise).
        Assert.IsType<InvalidOperationException>(Record.Exception(() => Pydantic.Wrap(() => throw new InvalidOperationException("x"))));
        Assert.IsType<NodeLabelValidationException>(Record.Exception(() => Pydantic.Wrap(() => throw new NodeLabelValidationException("x"))));
        var inner = new ValidationException("M", [new PydanticError(["a"], "missing", "Field required")]);
        Assert.Same(inner, Record.Exception(() => Pydantic.Wrap(() => throw inner)));
        Assert.IsAssignableFrom<ArgumentException>(Record.Exception(() => Pydantic.Wrap(() => throw new ArgumentException("bad"))));

        // Python's IndexError and TypeError are not ValueError. Their C# forms derive from ArgumentException.
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => Pydantic.Wrap(() => throw new ArgumentOutOfRangeException("i"))), exactMatch: true);
        Assert.IsType<ArgumentNullException>(Record.Exception(() => Pydantic.Wrap(() => throw new ArgumentNullException("x"))), exactMatch: true);
    }

    [Fact]
    public void Dump_keeps_a_value_outside_the_value_model_and_JsonValue_rejects_it()
    {
        // model_dump() returns an object it does not know as it is. model_dump(mode='json') raises
        // PydanticSerializationError: "Unable to serialize unknown type".
        var unknown = new Uri("https://example.com");
        Assert.Same(unknown, Pydantic.Dump(unknown));
        Assert.Same(unknown, Pydantic.DumpFields(false, ("x", unknown))["x"]);
        var ex = Assert.IsType<ArgumentException>(Record.Exception(() => Pydantic.JsonValue(unknown)), exactMatch: true);
        Assert.Equal("Unable to serialize unknown type: System.Uri", ex.Message);
        Assert.IsType<ArgumentException>(Record.Exception(() => Pydantic.DumpFields(true, ("x", unknown))), exactMatch: true);
    }

    [Fact]
    public void Setter_of_a_validated_field_validates_as_validate_assignment_does()
    {
        var node = EntityNode.ModelValidate(new OrderedDictionary<string, object?> { ["name"] = "A", ["group_id"] = "g" });
        var ex = Assert.IsType<ValidationException>(Record.Exception(() => node.Labels = ["ok", "not ok"]), exactMatch: true);
        Assert.Equal("labels", ex.Errors.Single().Loc.Single());
        Assert.Empty(node.Labels);
    }

    [Fact]
    public void JsonValue_matches_model_dump_json_mode()
    {
        ForEach(List("json_value"), c =>
        {
            var input = new Decoder().Decode(c.GetProperty("v"));
            if (c.TryGetProperty("e", out _))
            {
                var ex = Assert.IsType<DecoderFallbackException>(Record.Exception(() => Pydantic.JsonValue(input)), exactMatch: true);
                Assert.Equal(Text(c.GetProperty("msg")), ex.Message);
            }
            else
            {
                AssertValue(new Decoder().Decode(c.GetProperty("r")), Pydantic.JsonValue(input), c.GetProperty("v").ToString());
            }
        });
    }

    [Fact]
    public void Callable_keeps_a_delegate_of_the_field_type()
    {
        // pydantic's callable check accepts each callable object. The values file cannot hold a function.
        Func<string, bool> f = s => s.Length > 0;
        Assert.Same(f, Pydantic.Callable<Func<string, bool>>(f));
        var ex = Assert.Throws<ValidationException>(() => Pydantic.Callable<Func<string, bool>>(new Func<int, bool>(i => i > 0)));
        Assert.Equal("callable_type", ex.Errors.Single().Type);
    }

    [Fact]
    public void PydanticValue_of_a_field_with_an_error_throws()
    {
        var f = new PydanticFields("M", []);
        var x = f.Required("x", Pydantic.Str);
        Assert.Throws<InvalidOperationException>(() => x.Value);
        var ex = Assert.Throws<ValidationException>(f.ThrowIfAny);
        Assert.Equal("1 validation error for M\nx\n  Field required [type=missing]", ex.Message);
    }

    private static JsonElement ToJson(PydanticError e) =>
        JsonSerializer.SerializeToElement(new object[] { e.Loc, e.Type, e.Msg });
}
