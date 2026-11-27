namespace Graphiti.Internal;

using Graphiti.PyCompat;

/// <summary>
/// The scalar kind of a field of a custom entity or edge type (decision D26). Each member of the public
/// <see cref="Graphiti.GraphFieldKind"/> is one of these kinds, with <see cref="GraphFieldDescriptor.IsList"/> for a list form.
/// </summary>
internal enum GraphFieldKind
{
    String,
    Integer,
    Number,
    Boolean,
}

/// <summary>
/// One field of a custom entity or edge type: what Python reads from <c>type_model.model_fields[name]</c> and from
/// the annotation of the field (RULES.md R15).
/// </summary>
/// <param name="Name">The Python field name. It is a wire name: it reaches the prompt and the stored property.</param>
/// <param name="Kind">The scalar kind of the field, or of each item if <paramref name="IsList"/> is true.</param>
/// <param name="IsList">True for a <c>list[kind]</c> field.</param>
/// <param name="Description">The <c>description</c> of the field, or null.</param>
/// <param name="IsRequired"><c>field_info.is_required()</c>. A field that is not required has the default <c>None</c>.</param>
/// <param name="MaxLength">The <c>max_length</c> of a string field, or null.</param>
internal sealed record GraphFieldDescriptor(
    string Name,
    GraphFieldKind Kind,
    bool IsList,
    string? Description,
    bool IsRequired,
    long? MaxLength);

/// <summary>
/// A custom entity or edge type, where Python passes a <c>type[BaseModel]</c> that the caller wrote (GAPS G6,
/// decision D26, RULES.md R15). The public <c>GraphTypeDefinition</c> builds it. It is a response model, because
/// upstream passes a custom type as the response model of the attribute prompt.
/// </summary>
internal sealed class GraphTypeDescriptor : IResponseModel
{
    /// <summary>Initializes a new instance of the <see cref="GraphTypeDescriptor"/> class.</summary>
    /// <param name="pythonName">The class name, which is also the key of the caller's dict.</param>
    /// <param name="doc">The docstring of the class, or null for a class with no docstring.</param>
    /// <param name="fields">The fields in the order of the class.</param>
    internal GraphTypeDescriptor(string pythonName, string? doc, IEnumerable<GraphFieldDescriptor> fields)
    {
        PythonName = pythonName;
        Doc = doc;
        foreach (var field in fields)
        {
            ModelFields.Add(field.Name, field);
        }

        ModelJsonSchema = BuildSchema();
    }

    /// <summary>Gets <c>type_model.__name__</c>.</summary>
    public string PythonName { get; }

    /// <summary>Gets <c>type_model.__doc__</c>.</summary>
    internal string? Doc { get; }

    /// <summary>Gets <c>type_model.model_fields</c>: the fields by Python name, in the order of the class.</summary>
    internal OrderedDictionary<string, GraphFieldDescriptor> ModelFields { get; } = new(StringComparer.Ordinal);

    // TODO(port): pydantic writes "maxLength" into the schema of a str field with max_length. PydanticType has no member
    // for it, and only the lead changes PyCompat, so the schema of such a field has no maxLength key. ModelValidate
    // checks the limit.

    /// <summary>
    /// Gets <c>type_model.model_json_schema()</c>: a required field is <c>x: T = Field(...)</c>, and a field that is
    /// not required is <c>x: T | None = Field(default=None)</c>. The description of the schema is <see cref="Doc"/>
    /// as it is: pydantic writes <c>inspect.cleandoc</c> of the docstring, which equals it for a docstring with no
    /// indent and no tab (decision D26). An empty docstring gives no description, as in pydantic.
    /// </summary>
    public PydanticSchema ModelJsonSchema { get; }

    /// <summary>
    /// <c>type_model(**data)</c>, then <c>model_dump()</c>: applies lax coercion by the kind of each field, and returns
    /// each field in field order. A field that is not required and not in <paramref name="data"/> is <c>None</c>. A
    /// key that is not a field is ignored, which is pydantic's default.
    /// </summary>
    /// <param name="data">The open data.</param>
    /// <exception cref="ValidationException">A value does not fit the kind of its field, a text is longer than the
    /// <c>max_length</c> of its field, or a required field is missing.</exception>
    internal OrderedDictionary<string, object?> ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields(PythonName, data);
        var values = new List<(string Name, PydanticValue<object?> Value)>(ModelFields.Count);
        foreach (var (name, field) in ModelFields)
        {
            var read = Reader(field);
            values.Add((name, field.IsRequired ? f.Required(name, read) : f.Default(name, v => v is null ? null : read(v), () => null)));
        }

        f.ThrowIfAny();
        var result = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            result[name] = value.Value;
        }

        return result;
    }

    // The reader of one field: the lax reader of its kind, of each item for a list, and the max_length check of a
    // str field inside the read, so that its error stands in field order with the other errors, as in pydantic.
    private static Func<object?, object?> Reader(GraphFieldDescriptor field)
    {
        Func<object?, object?> item = field.Kind switch
        {
            GraphFieldKind.String when field.MaxLength is { } limit && !field.IsList => v => MaxLength(Pydantic.Str(v), limit),
            GraphFieldKind.String => v => Pydantic.Str(v),
            GraphFieldKind.Integer => v => Pydantic.Int(v),
            GraphFieldKind.Number => v => Pydantic.Float(v),
            GraphFieldKind.Boolean => v => Pydantic.Bool(v),
            _ => throw new InvalidOperationException($"GraphFieldKind {field.Kind}"),
        };
        return field.IsList ? v => Pydantic.List(v, item) : item;
    }

    // Field(max_length=limit): the check of PydanticFields, moved to the location of the value. The field read puts it
    // under the field name. The title of the inner check is not shown.
    private static string MaxLength(string value, long limit)
    {
        var check = new PydanticFields(string.Empty);
        check.MaxLength(string.Empty, value, limit);
        try
        {
            check.ThrowIfAny();
        }
        catch (ValidationException e)
        {
            throw new ValidationException(null, [.. e.Errors.Select(error => error with { Loc = [] })]);
        }

        return value;
    }

    private PydanticSchema BuildSchema()
    {
        var schema = new PydanticSchema(PythonName, string.IsNullOrEmpty(Doc) ? null : Doc);
        foreach (var (name, field) in ModelFields)
        {
            var scalar = field.Kind switch
            {
                GraphFieldKind.String => PydanticType.Str,
                GraphFieldKind.Integer => PydanticType.Int,
                GraphFieldKind.Number => PydanticType.Float,
                GraphFieldKind.Boolean => PydanticType.Bool,
                _ => throw new InvalidOperationException($"GraphFieldKind {field.Kind}"),
            };
            var type = field.IsList ? PydanticType.List(scalar) : scalar;
            schema = field.IsRequired
                ? schema.Required(name, type, field.Description)
                : schema.Default(name, PydanticType.Optional(type), null, field.Description);
        }

        return schema;
    }
}
