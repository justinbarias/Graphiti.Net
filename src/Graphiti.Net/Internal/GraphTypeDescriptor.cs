namespace Graphiti.Internal;

using Graphiti.PyCompat;

/// <summary>The scalar kind of a field of a custom entity or edge type (decision D26).</summary>
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
    }

    /// <summary>Gets <c>type_model.__name__</c>.</summary>
    public string PythonName { get; }

    /// <summary>Gets <c>type_model.__doc__</c>.</summary>
    internal string? Doc { get; }

    /// <summary>Gets <c>type_model.model_fields</c>: the fields by Python name, in the order of the class.</summary>
    internal OrderedDictionary<string, GraphFieldDescriptor> ModelFields { get; } = new(StringComparer.Ordinal);

    // TODO(port): the schema of a custom type. The lead builds it with the judge fixtures (debt T18).

    /// <summary>Gets <c>type_model.model_json_schema()</c>.</summary>
    public PydanticSchema ModelJsonSchema => throw new NotSupportedException("TODO(port): GraphTypeDescriptor.model_json_schema");

    // TODO(port): the validation of a custom type. The lead builds it with the judge fixtures (debt T18).

    /// <summary>
    /// <c>type_model(**data)</c>: applies lax coercion by the kind of each field, and returns the validated dictionary
    /// in field order.
    /// </summary>
    /// <param name="data">The open data.</param>
    /// <exception cref="ValidationException">A value does not fit the kind of its field, or a required field is missing.</exception>
    internal OrderedDictionary<string, object?> ModelValidate(OrderedDictionary<string, object?> data) =>
        throw new NotSupportedException("TODO(port): GraphTypeDescriptor.__init__");
}
