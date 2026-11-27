namespace Graphiti;

using System.Diagnostics.Contracts;
using Graphiti.Internal;

/// <summary>
/// A custom entity type or edge type, where upstream takes a pydantic class (decision D26). Extraction fills the
/// fields of the type as attributes of a node or an edge. Each <c>Add</c> member returns a new definition with one more
/// field, so a definition never changes.
/// </summary>
/// <example>
/// <code>
/// var person = GraphTypeDefinition.Create("Person", "A human being who is named in the text.")
///     .AddString("occupation", "The job or profession of the person")
///     .AddString("home_city", "The city where the person lives");
/// </code>
/// </example>
/// <remarks>
/// The definition gives what upstream reads from the class: the docstring, the field list, the JSON schema text, and the
/// validation of a model reply. <see cref="Description"/> is the raw docstring, as <c>__doc__</c> is. The JSON schema
/// holds <c>inspect.cleandoc</c> of it, as pydantic writes it, so the schema text equals pydantic's. A field kind
/// outside <see cref="GraphFieldKind"/> is not possible. Two definitions are equal if they have the same name, the
/// same description, and equal fields in the same order.
/// </remarks>
public sealed record GraphTypeDefinition
{
    // The members of BaseModel in the protected namespaces "model_validate" and "model_dump" (pydantic raises ValueError
    // for a field with such a name), and model_config (pydantic raises TypeError).
    private static readonly string[] RefusedNames =
        ["model_config", "model_dump", "model_dump_json", "model_validate", "model_validate_json", "model_validate_strings"];

    private readonly GraphTypeDescriptor descriptor;

    private GraphTypeDefinition(string name, string? description, IReadOnlyList<GraphFieldDefinition> fields)
    {
        Name = name;
        Description = description;
        Fields = fields;
        descriptor = new GraphTypeDescriptor(
            name,
            description,
            fields.Select(field => new GraphFieldDescriptor(
                field.Name,
                ScalarKind(field.Kind),
                field.Kind is GraphFieldKind.StringList or GraphFieldKind.IntegerList or GraphFieldKind.NumberList or GraphFieldKind.BooleanList,
                field.Description,
                field.Required,
                field.MaxLength)));
    }

    /// <summary>
    /// Gets the name of the type. It is both the key of the type in the upstream dict and the Python class name, which
    /// the JSON schema holds as its title. It reaches the prompt, and the label of a node or the name of an edge.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the description of the type, which is the docstring of the class, or null for a class with no docstring.</summary>
    public string? Description { get; }

    /// <summary>Gets the fields in the order of the class. The order reaches the prompt and the JSON schema.</summary>
    public IReadOnlyList<GraphFieldDefinition> Fields { get; }

    /// <summary>Creates a definition with no field.</summary>
    /// <param name="name">The name of the type: the dict key and the Python class name.</param>
    /// <param name="description">
    /// The docstring of the class. Null is a class with no docstring: the schema then has no <c>description</c>, and
    /// upstream's text <c>Default Entity Type</c> applies.
    /// </param>
    /// <returns>The new definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    [Pure]
    public static GraphTypeDefinition Create(string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new GraphTypeDefinition(name, description, []);
    }

    /// <summary>Adds a text field: <c>str</c>, or <c>str | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name, for example <c>home_city</c>.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <param name="maxLength">The <c>max_length</c> of the field in code points, or null.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is negative.</exception>
    [Pure]
    public GraphTypeDefinition AddString(string name, string? description = null, bool required = false, int? maxLength = null)
    {
        if (maxLength is { } limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(limit, nameof(maxLength));
        }

        return Add(name, GraphFieldKind.String, description, required, maxLength);
    }

    /// <summary>Adds a whole number field: <c>int</c>, or <c>int | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddInteger(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.Integer, description, required, null);

    /// <summary>Adds a number field: <c>float</c>, or <c>float | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddNumber(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.Number, description, required, null);

    /// <summary>Adds a truth value field: <c>bool</c>, or <c>bool | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddBoolean(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.Boolean, description, required, null);

    /// <summary>Adds a list of texts: <c>list[str]</c>, or <c>list[str] | None = None</c> if it is not required. It has no length limit.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddStringList(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.StringList, description, required, null);

    /// <summary>Adds a list of whole numbers: <c>list[int]</c>, or <c>list[int] | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddIntegerList(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.IntegerList, description, required, null);

    /// <summary>Adds a list of numbers: <c>list[float]</c>, or <c>list[float] | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddNumberList(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.NumberList, description, required, null);

    /// <summary>Adds a list of truth values: <c>list[bool]</c>, or <c>list[bool] | None = None</c> if it is not required.</summary>
    /// <param name="name">The Python field name.</param>
    /// <param name="description">The description of the field, or null.</param>
    /// <param name="required">True for a required field. A field that is not required has the default <c>None</c>.</param>
    /// <returns>A new definition with the field at the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The definition has a field with the name already, or pydantic refuses the
    /// name: it starts with <c>_</c>, or it is <c>model_config</c> or a member of <c>BaseModel</c> that starts with
    /// <c>model_dump</c> or <c>model_validate</c>.</exception>
    [Pure]
    public GraphTypeDefinition AddBooleanList(string name, string? description = null, bool required = false) =>
        Add(name, GraphFieldKind.BooleanList, description, required, null);

    /// <summary>
    /// The descriptor that ported code takes where Python passes the class (RULES.md R15). A definition has one
    /// descriptor, so that the class is one object, as in Python.
    /// </summary>
    internal GraphTypeDescriptor ToDescriptor() => descriptor;

    /// <summary>
    /// Compares the name, the description, and the fields in order (decision D25). The descriptor that the definition
    /// builds is not part of the comparison.
    /// </summary>
    /// <param name="other">The definition to compare with.</param>
    /// <returns>True if the two definitions are equal.</returns>
    public bool Equals(GraphTypeDefinition? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && Fields.SequenceEqual(other.Fields);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }

    private static Internal.GraphFieldKind ScalarKind(GraphFieldKind kind) => kind switch
    {
        GraphFieldKind.String or GraphFieldKind.StringList => Internal.GraphFieldKind.String,
        GraphFieldKind.Integer or GraphFieldKind.IntegerList => Internal.GraphFieldKind.Integer,
        GraphFieldKind.Number or GraphFieldKind.NumberList => Internal.GraphFieldKind.Number,
        GraphFieldKind.Boolean or GraphFieldKind.BooleanList => Internal.GraphFieldKind.Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private GraphTypeDefinition Add(string name, GraphFieldKind kind, string? description, bool required, int? maxLength)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.StartsWith('_'))
        {
            throw new ArgumentException($"The field name {name} starts with '_'. Pydantic does not accept it as a field name.", nameof(name));
        }

        if (RefusedNames.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The field name {name} is a member of the pydantic BaseModel. Pydantic does not accept it as a field name.", nameof(name));
        }

        if (Fields.Any(field => field.Name == name))
        {
            throw new ArgumentException($"The type {Name} has a field {name} already.", nameof(name));
        }

        return new GraphTypeDefinition(Name, Description, [.. Fields, new GraphFieldDefinition(name, kind, description, required, maxLength)]);
    }
}
