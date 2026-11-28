namespace Graphiti.Internal;

using System.Collections.ObjectModel;
using Graphiti.PyCompat;
using CoreUtils = Graphiti.Core.Utils;

/// <summary>
/// The defaults of the public records, and the value model of <c>attributes</c> and <c>episode_metadata</c> (the public
/// surface design, "Public records"). A public value is null, <see cref="bool"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="string"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, a read-only list,
/// or a read-only dictionary with string keys. A tuple becomes a read-only list. Each other kind of value throws
/// <see cref="InvalidCastException"/>.
/// </summary>
internal static class PublicValues
{
    private const string Kinds =
        "null, bool, long, double, string, DateTimeOffset, DateOnly, a read-only list, or a read-only dictionary with string keys";

    /// <summary>The default of an <c>attributes</c> property: an empty dictionary.</summary>
    internal static readonly IReadOnlyDictionary<string, object?> EmptyAttributes = ReadOnlyDictionary<string, object?>.Empty;

    /// <summary><c>str(uuid4())</c>, the default of each <c>uuid</c> field.</summary>
    internal static string NewUuid() => Py.Uuid4();

    /// <summary><c>utc_now()</c>, the default of <c>created_at</c> on a node: the clock of the port, cut to microseconds.</summary>
    internal static DateTimeOffset UtcNow() => CoreUtils.DatetimeUtils.UtcNow().ToDateTimeOffset();

    /// <summary>A public date as a ported date. The offset stays, and the value is cut to microseconds.</summary>
    internal static PyDateTime ToPortedDate(DateTimeOffset value) => PyDateTime.FromDateTimeOffset(value);

    /// <summary>A ported date as a public date. A naive value is read as UTC (decision D36).</summary>
    internal static DateTimeOffset ToPublicDate(PyDateTime value) => value.ToDateTimeOffset();

    /// <summary>A public dictionary of the value model as a ported dictionary, in the order of the public dictionary.</summary>
    internal static OrderedDictionary<string, object?> ToPortedDict(IReadOnlyDictionary<string, object?> values)
    {
        var result = new OrderedDictionary<string, object?>(values.Count);
        foreach (var (key, value) in values)
        {
            result[key] = ToPortedValue(value);
        }

        return result;
    }

    /// <summary>A ported dictionary of the value model as a public read-only dictionary, in the same order.</summary>
    internal static IReadOnlyDictionary<string, object?> ToPublicDict(OrderedDictionary<string, object?> values)
    {
        var result = new OrderedDictionary<string, object?>(values.Count);
        foreach (var (key, value) in values)
        {
            result[key] = ToPublicValue(value);
        }

        return new ReadOnlyDictionary<string, object?>(result);
    }

    /// <summary>One public value as a ported value of the Python value model (rule R6).</summary>
    internal static object? ToPortedValue(object? value) => value switch
    {
        null => null,
        bool or long or double or string or DateOnly => value,
        DateTimeOffset date => ToPortedDate(date),
        IReadOnlyDictionary<string, object?> map => ToPortedDict(map),
        IReadOnlyList<object?> list => list.Select(ToPortedValue).ToList(),
        _ => throw new InvalidCastException(
            $"A value of the type {value.GetType().FullName} has no form in Graphiti. A value is {Kinds}."),
    };

    /// <summary>One ported value of the Python value model as a public value.</summary>
    internal static object? ToPublicValue(object? value) => value switch
    {
        null => null,
        bool or long or double or string or DateOnly => value,
        PyDateTime date => ToPublicDate(date),
        OrderedDictionary<string, object?> map => ToPublicDict(map),
        List<object?> list => list.Select(ToPublicValue).ToList().AsReadOnly(),
        PyTuple tuple => tuple.Select(ToPublicValue).ToList().AsReadOnly(),
        _ => throw new InvalidCastException(
            $"A value of the Python type '{Py.TypeName(value)}' has no public form. A value is {Kinds}."),
    };
}
