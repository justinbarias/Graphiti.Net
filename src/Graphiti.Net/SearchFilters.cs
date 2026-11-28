namespace Graphiti;

/// <summary>
/// The filters of a search. The public form of upstream's <c>SearchFilters</c>. A date filter list is a list of
/// groups: the filters of one group must all hold, and one group must hold.
/// </summary>
public sealed record SearchFilters
{
    /// <summary>
    /// Gets the node labels to filter on, or null. A label that is not safe for a query throws
    /// <see cref="ArgumentException"/> at the call.
    /// </summary>
    public IReadOnlyList<string>? NodeLabels { get; init; }

    /// <summary>Gets the edge types to filter on, or null.</summary>
    public IReadOnlyList<string>? EdgeTypes { get; init; }

    /// <summary>Gets the filters of <c>valid_at</c>, or null.</summary>
    public IReadOnlyList<IReadOnlyList<DateFilter>>? ValidAt { get; init; }

    /// <summary>Gets the filters of <c>invalid_at</c>, or null.</summary>
    public IReadOnlyList<IReadOnlyList<DateFilter>>? InvalidAt { get; init; }

    /// <summary>Gets the filters of <c>created_at</c>, or null.</summary>
    public IReadOnlyList<IReadOnlyList<DateFilter>>? CreatedAt { get; init; }

    /// <summary>Gets the filters of <c>expired_at</c>, or null.</summary>
    public IReadOnlyList<IReadOnlyList<DateFilter>>? ExpiredAt { get; init; }

    /// <summary>Gets the edge UUIDs to filter on, or null.</summary>
    public IReadOnlyList<string>? EdgeUuids { get; init; }

    /// <summary>Gets the source node UUIDs that restrict the edge results, or null.</summary>
    public IReadOnlyList<string>? EdgeSourceNodeUuids { get; init; }

    /// <summary>Gets the target node UUIDs that restrict the edge results, or null.</summary>
    public IReadOnlyList<string>? EdgeTargetNodeUuids { get; init; }

    /// <summary>
    /// Gets the property filters, or null. Upstream 0.32.0 reads this field nowhere, so it has no effect on a search.
    /// </summary>
    public IReadOnlyList<PropertyFilter>? PropertyFilters { get; init; }
}

/// <summary>A filter on a date. The public form of upstream's <c>DateFilter</c>.</summary>
public sealed record DateFilter
{
    /// <summary>Gets the date to filter on, or null. A call keeps the offset and cuts the value to microseconds.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Gets the comparison operator of the filter.</summary>
    public required ComparisonOperator ComparisonOperator { get; init; }
}

/// <summary>A filter on a property. The public form of upstream's <c>PropertyFilter</c>.</summary>
public sealed record PropertyFilter
{
    /// <summary>Gets the name of the property.</summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the value to match on, or null. A value is a <see cref="string"/>, a <see cref="long"/>, or a
    /// <see cref="double"/>, as upstream's <c>str | int | float | None</c>. A call throws
    /// <see cref="InvalidCastException"/> for another kind of value.
    /// </summary>
    public object? PropertyValue { get; init; }

    /// <summary>Gets the comparison operator of the filter.</summary>
    public required ComparisonOperator ComparisonOperator { get; init; }
}

/// <summary>A comparison operator of a filter. The public form of upstream's <c>ComparisonOperator</c>.</summary>
public enum ComparisonOperator
{
    /// <summary>The value equals the operand: <c>=</c>.</summary>
    Equals = 0,

    /// <summary>The value differs from the operand: <c>&lt;&gt;</c>.</summary>
    NotEquals = 1,

    /// <summary>The value is greater than the operand: <c>&gt;</c>.</summary>
    GreaterThan = 2,

    /// <summary>The value is less than the operand: <c>&lt;</c>.</summary>
    LessThan = 3,

    /// <summary>The value is greater than or equal to the operand: <c>&gt;=</c>.</summary>
    GreaterThanEqual = 4,

    /// <summary>The value is less than or equal to the operand: <c>&lt;=</c>.</summary>
    LessThanEqual = 5,

    /// <summary>The value is null: <c>IS NULL</c>.</summary>
    IsNull = 6,

    /// <summary>The value is not null: <c>IS NOT NULL</c>.</summary>
    IsNotNull = 7,
}
