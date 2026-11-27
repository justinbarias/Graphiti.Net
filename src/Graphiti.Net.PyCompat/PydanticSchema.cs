namespace Graphiti.PyCompat;

/// <summary>
/// The JSON schema of one model, as <c>M.model_json_schema()</c> of pydantic 2.12.5 gives it (RULES.md R7, GAPS G3).
/// A ported model builds it in field order with the Python field names, the Python class name as the title, and the
/// <c>inspect.cleandoc</c> text of the docstring as the description. <see cref="ToDict"/> gives the dict, and
/// <c>json.dumps(M.model_json_schema())</c> is <c>PyJson.Dumps(M.ModelJsonSchema.ToDict())</c>.
/// </summary>
/// <example>
/// <code>
/// internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("NodeResolutions")
///     .Required("entity_resolutions", PydanticType.List(PydanticType.Model(NodeDuplicate.ModelJsonSchema)), "List of resolved nodes");
/// </code>
/// </example>
internal sealed class PydanticSchema
{
    private readonly List<(string Name, PydanticType Type, string? Description, bool Required, bool HasDefault, object? Default)> fields = [];

    /// <summary>Initializes a new instance of the <see cref="PydanticSchema"/> class.</summary>
    /// <param name="title">The Python class name.</param>
    /// <param name="description"><c>inspect.cleandoc(M.__doc__)</c> of a class with its own docstring, else null.</param>
    internal PydanticSchema(string title, string? description = null)
    {
        Title = title;
        Description = description;
    }

    /// <summary>Gets the Python class name, the <c>title</c> of the schema.</summary>
    internal string Title { get; }

    /// <summary>Gets the description of the schema, or null.</summary>
    internal string? Description { get; }

    /// <summary>A required field: <c>x: T</c> or <c>x: T = Field(..., description=...)</c>. It is in <c>required</c>.</summary>
    internal PydanticSchema Required(string name, PydanticType type, string? description = null)
    {
        fields.Add((name, type, description, true, false, null));
        return this;
    }

    /// <summary>
    /// A field with a default value: <c>= x</c> or <c>Field(default=x)</c>. The schema holds the default as
    /// <see cref="Pydantic.JsonValue(object?)"/> gives it. Pass the value as Python has it: <c>Field(default=0)</c> on a
    /// float field is the int <c>0L</c>, which the schema writes as <c>0</c>.
    /// </summary>
    internal PydanticSchema Default(string name, PydanticType type, object? value, string? description = null)
    {
        fields.Add((name, type, description, false, true, value));
        return this;
    }

    /// <summary>A field with <c>Field(default_factory=...)</c>: not required, and the schema has no <c>default</c>.</summary>
    internal PydanticSchema Factory(string name, PydanticType type, string? description = null)
    {
        fields.Add((name, type, description, false, false, null));
        return this;
    }

    /// <summary><c>M.model_json_schema()</c>: the dict with <c>$defs</c> for each nested model and enum, keys sorted as pydantic sorts them.</summary>
    internal OrderedDictionary<string, object?> ToDict()
    {
        var defs = new SortedDictionary<string, OrderedDictionary<string, object?>>(StringComparer.Ordinal);
        var body = Body(defs);
        if (defs.Count > 0)
        {
            var d = new OrderedDictionary<string, object?>();
            foreach (var (name, def) in defs)
            {
                d[name] = def;
            }

            body["$defs"] = d;
        }

        return Sort(body, null) as OrderedDictionary<string, object?> ?? body;
    }

    /// <summary>The schema of the model without <c>$defs</c>. Each nested model and enum goes into <paramref name="defs"/>.</summary>
    internal OrderedDictionary<string, object?> Body(IDictionary<string, OrderedDictionary<string, object?>> defs)
    {
        var properties = new OrderedDictionary<string, object?>();
        var required = new List<object?>();
        foreach (var field in fields)
        {
            var schema = field.Type.Build(defs);
            if (!field.Type.IsReference)
            {
                schema["title"] = FieldTitle(field.Name);
            }

            if (field.Description is not null)
            {
                schema["description"] = field.Description;
            }

            if (field.HasDefault)
            {
                schema["default"] = Pydantic.JsonValue(field.Default);
            }

            properties[field.Name] = schema;
            if (field.Required)
            {
                required.Add(field.Name);
            }
        }

        var body = new OrderedDictionary<string, object?>();
        if (Description is not null)
        {
            body["description"] = Description;
        }

        body["properties"] = properties;
        if (required.Count > 0)
        {
            body["required"] = required;
        }

        body["title"] = Title;
        body["type"] = "object";
        return body;
    }

    /// <summary>
    /// pydantic's field title, <c>GenerateJsonSchema.get_title_from_name</c>: <c>name.title().replace('_', ' ').strip()</c>.
    /// </summary>
    private static string FieldTitle(string name) => PyText.Strip(PyText.Title(name).Replace('_', ' '));

    /// <summary>pydantic's sort of a schema: the keys of each dict in code point order, except under <c>properties</c> and <c>default</c>.</summary>
    private static object? Sort(object? value, string? parentKey)
    {
        switch (value)
        {
            case OrderedDictionary<string, object?> dict:
                var keys = parentKey is "properties" or "default" ? dict.Keys.ToList() : [.. dict.Keys.Order(StringComparer.Ordinal)];
                var result = new OrderedDictionary<string, object?>();
                foreach (var key in keys)
                {
                    result[key] = Sort(dict[key], key);
                }

                return result;
            case List<object?> list:
                return list.Select(item => Sort(item, parentKey)).ToList();
            default:
                return value;
        }
    }
}

/// <summary>
/// The type of a field in a <see cref="PydanticSchema"/>, with pydantic's JSON schema for it. A model or an enum is a
/// <c>$ref</c> to its entry in <c>$defs</c>, and a field of that type, or of that type <c>| None</c>, has no title.
/// </summary>
internal sealed class PydanticType
{
    private readonly Func<IDictionary<string, OrderedDictionary<string, object?>>, OrderedDictionary<string, object?>> build;

    private PydanticType(Func<IDictionary<string, OrderedDictionary<string, object?>>, OrderedDictionary<string, object?>> build, bool isReference = false)
    {
        this.build = build;
        IsReference = isReference;
    }

    /// <summary><c>str</c>.</summary>
    internal static PydanticType Str { get; } = Simple("string");

    /// <summary><c>str</c> with <c>Field(max_length=n)</c>: a string with <c>maxLength</c>.</summary>
    internal static PydanticType StrMaxLength(long maxLength) =>
        new(_ => new() { ["maxLength"] = maxLength, ["type"] = "string" });

    /// <summary><c>int</c>.</summary>
    internal static PydanticType Int { get; } = Simple("integer");

    /// <summary><c>float</c>.</summary>
    internal static PydanticType Float { get; } = Simple("number");

    /// <summary><c>bool</c>.</summary>
    internal static PydanticType Bool { get; } = Simple("boolean");

    /// <summary><c>datetime</c>: a string with the format <c>date-time</c>.</summary>
    internal static PydanticType DateTime { get; } = new(_ => new() { ["format"] = "date-time", ["type"] = "string" });

    /// <summary><c>Any</c>: no type.</summary>
    internal static PydanticType Any { get; } = new(_ => []);

    /// <summary>Gets a value indicating whether the type is a <c>$ref</c>, so that its field has no title.</summary>
    internal bool IsReference { get; }

    /// <summary><c>list[T]</c>.</summary>
    internal static PydanticType List(PydanticType item) =>
        new(defs => new() { ["items"] = item.Build(defs), ["type"] = "array" });

    /// <summary><c>dict[str, T]</c>. A value type of <c>Any</c> gives <c>additionalProperties: true</c>.</summary>
    internal static PydanticType Dict(PydanticType value) => new(defs =>
    {
        var schema = value.Build(defs);
        return new() { ["additionalProperties"] = schema.Count == 0 ? true : schema, ["type"] = "object" };
    });

    /// <summary><c>T | None</c>: <c>anyOf</c> of the type and <c>null</c>.</summary>
    internal static PydanticType Optional(PydanticType type) => Union(type, Simple("null")).WithReference(type.IsReference);

    /// <summary><c>A | B | ...</c>: <c>anyOf</c> in the order of the annotation. Write <c>None</c> last as <c>PydanticType.Null</c>.</summary>
    internal static PydanticType Union(params PydanticType[] types) =>
        new(defs => new() { ["anyOf"] = types.Select(t => (object?)t.Build(defs)).ToList() });

    /// <summary>The <c>None</c> member of a union.</summary>
    internal static PydanticType Null { get; } = Simple("null");

    /// <summary><c>Literal['a']</c> with one text: <c>const</c>.</summary>
    internal static PydanticType Literal(string value) => new(_ => new() { ["const"] = value, ["type"] = "string" });

    /// <summary>
    /// A plain <c>Enum</c> class with text values: a <c>$ref</c> to a <c>$defs</c> entry with the values in member order.
    /// <paramref name="description"/> is <c>inspect.cleandoc</c> of the enum's docstring, or null.
    /// </summary>
    internal static PydanticType Enum(string title, IReadOnlyList<string> values, string? description = null) => new(
        defs =>
        {
            var def = new OrderedDictionary<string, object?>();
            if (description is not null)
            {
                def["description"] = description;
            }

            def["enum"] = values.Select(v => (object?)v).ToList();
            def["title"] = title;
            def["type"] = "string";
            defs[title] = def;
            return Ref(title);
        },
        isReference: true);

    /// <summary><see cref="Enum(string, IReadOnlyList{string}, string?)"/> of a C# enum with <see cref="PyEnumMemberAttribute"/> text values.</summary>
    internal static PydanticType Enum<T>(string? description = null)
        where T : struct, System.Enum =>
        Enum(typeof(T).Name, [.. System.Enum.GetValues<T>().Select(m => (string)PyEnum.Value(m))], description);

    /// <summary>A nested model: a <c>$ref</c> to its schema in <c>$defs</c>, with the models that it holds.</summary>
    internal static PydanticType Model(PydanticSchema schema) => new(
        defs =>
        {
            if (!defs.ContainsKey(schema.Title))
            {
                defs[schema.Title] = [];
                defs[schema.Title] = schema.Body(defs);
            }

            return Ref(schema.Title);
        },
        isReference: true);

    /// <summary>The schema of the type. Each model and enum that it holds goes into <paramref name="defs"/>.</summary>
    internal OrderedDictionary<string, object?> Build(IDictionary<string, OrderedDictionary<string, object?>> defs) => build(defs);

    private static PydanticType Simple(string type) => new(_ => new() { ["type"] = type });

    private static OrderedDictionary<string, object?> Ref(string title) => new() { ["$ref"] = "#/$defs/" + title };

    private PydanticType WithReference(bool isReference) => new(build, isReference);
}
