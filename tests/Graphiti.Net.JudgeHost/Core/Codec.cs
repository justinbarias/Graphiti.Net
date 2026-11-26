namespace Graphiti.JudgeHost.Core;

using System.Globalization;
using System.Text.Json.Nodes;

/// <summary>
/// Encodes .NET values with the tags that port/judge/README.md defines.
/// It grows with the bindings. Add a helper here when two bindings need the same encoding.
/// </summary>
public static class Codec
{
    /// <summary>A vector as a JSON array of numbers.</summary>
    public static JsonArray Vector(IEnumerable<double> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);

    /// <summary>
    /// A point in time as Python's <c>datetime.isoformat()</c> writes it: microseconds appear only
    /// when they are not zero, and the offset is always written as <c>+HH:MM</c>.
    /// </summary>
    public static JsonObject DateTime(DateTimeOffset value)
    {
        var microseconds = value.Ticks % TimeSpan.TicksPerSecond / 10;
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        if (microseconds != 0)
        {
            text += "." + microseconds.ToString("D6", CultureInfo.InvariantCulture);
        }

        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        text += string.Create(CultureInfo.InvariantCulture, $"{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}");
        return new JsonObject { ["$dt"] = text };
    }

    /// <summary>
    /// The Python class name that a .NET exception stands for. The rulebook maps each ported
    /// exception type to its Python name. Bindings for a ported unit extend this map.
    /// </summary>
    public static string PythonExceptionName(Exception error) => error switch
    {
        ArgumentNullException => "TypeError",
        ArgumentException => "ValueError",
        KeyNotFoundException => "KeyError",
        IndexOutOfRangeException or ArgumentOutOfRangeException => "IndexError",
        InvalidCastException => "TypeError",
        NotImplementedException => "NotImplementedError",
        _ => error.GetType().Name,
    };
}
