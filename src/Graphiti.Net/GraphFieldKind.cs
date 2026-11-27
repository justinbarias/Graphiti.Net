namespace Graphiti;

/// <summary>
/// The kind of a field of a custom entity or edge type: one of the closed set of decision D26. Each kind is the
/// Python annotation that upstream reads from the pydantic class. A field that is not required adds <c>| None</c>.
/// </summary>
public enum GraphFieldKind
{
    /// <summary>A text: <c>str</c>.</summary>
    String,

    /// <summary>A whole number: <c>int</c>.</summary>
    Integer,

    /// <summary>A number: <c>float</c>.</summary>
    Number,

    /// <summary>A truth value: <c>bool</c>.</summary>
    Boolean,

    /// <summary>A list of texts: <c>list[str]</c>.</summary>
    StringList,

    /// <summary>A list of whole numbers: <c>list[int]</c>.</summary>
    IntegerList,

    /// <summary>A list of numbers: <c>list[float]</c>.</summary>
    NumberList,

    /// <summary>A list of truth values: <c>list[bool]</c>.</summary>
    BooleanList,
}
