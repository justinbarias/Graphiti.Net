namespace Graphiti;

/// <summary>
/// One field of a <see cref="GraphTypeDefinition"/>. The <c>Add</c> members of the definition make it.
/// </summary>
public sealed record GraphFieldDefinition
{
    internal GraphFieldDefinition(string name, GraphFieldKind kind, string? description, bool required, long? maxLength)
    {
        Name = name;
        Kind = kind;
        Description = description;
        Required = required;
        MaxLength = maxLength;
    }

    /// <summary>
    /// Gets the field name, for example <c>home_city</c>. It is the Python field name: it reaches the prompt, the JSON
    /// schema, and the stored property of the node or the edge.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the kind of the field.</summary>
    public GraphFieldKind Kind { get; }

    /// <summary>Gets the description of the field, which the JSON schema of the type holds, or null.</summary>
    public string? Description { get; }

    /// <summary>
    /// Gets a value indicating whether the field is required. A field that is not required has the default <c>None</c>,
    /// and its schema is <c>anyOf</c> of the kind and <c>null</c>.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Gets the <c>max_length</c> of a text field in code points, or null. Validation rejects a longer text. The
    /// attribute cap of a model reply also reads it, in place of its default limit.
    /// </summary>
    public long? MaxLength { get; }
}
