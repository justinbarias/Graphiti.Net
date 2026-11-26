namespace Graphiti.JudgeHost.Core;

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.PyCompat;

/// <summary>
/// Encodes results from the Python value model with the tags that port/judge/README.md defines. It follows
/// <c>encode</c> in <c>port/judge/host-python/codec.py</c> detail for detail. A binding encodes its result with
/// <see cref="Encode(object?)"/> and never writes an encoding by hand. A product type that the codec cannot know
/// is registered in <see cref="Bindings.Registry"/> with <see cref="JudgeRegistry.AddType{T}"/>.
/// </summary>
internal static partial class Codec
{
    /// <summary><c>{"ok": encode(value)}</c> for a binding that returns at once.</summary>
    internal static ValueTask<JsonNode?> Result(object? value) => ValueTask.FromResult(Encode(value));

    /// <summary>Encodes a value with the registry of the binding files.</summary>
    internal static JsonNode? Encode(object? value) => Encode(value, Bindings.Registry);

    /// <summary>Encodes a value. <paramref name="registry"/> gives the encoders of product types.</summary>
    internal static JsonNode? Encode(object? value, JudgeRegistry registry)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return node.Parent is null ? node : node.DeepClone();
            case bool b:
                return JsonValue.Create(b);
            case string s:
                return JsonValue.Create(s);
            case long or int or short or sbyte or byte or uint or ushort:
                return JsonValue.Create(System.Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
            case ulong or BigInteger:
                return Integer(value is ulong u ? u : (BigInteger)value);
            case double or float:
                return Float(System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
            case Enum member:
                // An enum member is encoded as its value.
                return Encode(PyEnum.Value(member), registry);
            case PyDateTime dt:
                return new JsonObject { ["$dt"] = dt.IsoFormat() };
            case DateTimeOffset offset:
                return DateTime(offset);
            case DateOnly date:
                return new JsonObject { ["$date"] = Py.Str(date) };
            case byte[] bytes:
                return new JsonObject { ["$bytes"] = System.Convert.ToHexStringLower(bytes) };
            case Type type:
                return Class(ClassName(type));
            case JudgeClass judgeClass:
                return Class(judgeClass.PythonName);
            case Exception error:
                return new JsonObject { ["$exc"] = PythonExceptionName(error) ?? throw NoPythonName(error.GetType()) };
            case IJudgeModel model:
                return Model(model.PythonName, model.JudgeFields, registry);
            case ITuple tuple:
                // A ValueTuple or a System.Tuple stands for a Python tuple, which results carry as a list.
                return new JsonArray([.. Enumerable.Range(0, tuple.Length).Select(index => Encode(tuple[index], registry))]);
        }

        if (registry.FindEncoder(value.GetType()) is { } encode)
        {
            return encode(value);
        }

        return value switch
        {
            IPySet set => Set(set.Items, registry),
            PyDict or IDictionary => Dict(Py.DictEntries(value), registry),
            _ when IsGenericSet(value.GetType()) => Set(((IEnumerable)value).Cast<object?>(), registry),
            IEnumerable items => new JsonArray([.. items.Cast<object?>().Select(item => Encode(item, registry))]),
            _ => new JsonObject { ["$unknown"] = value.GetType().Name },
        };
    }

    /// <summary>
    /// A model or a dataclass: <c>{"$type": pythonName, ...fields}</c>. The fields are the Python field names in field
    /// order with the attribute values. Each value is encoded, so a nested model encodes itself. A field that comes
    /// again takes the new value at its first position, as <c>dict.update</c> does with pydantic's extra fields.
    /// </summary>
    internal static JsonObject Model(string pythonName, IEnumerable<KeyValuePair<string, object?>> fields, JudgeRegistry? registry = null)
    {
        var result = new JsonObject { ["$type"] = pythonName };
        foreach (var (name, value) in fields)
        {
            result[name] = Encode(value, registry ?? Bindings.Registry);
        }

        return result;
    }

    /// <summary>A class that a function returned: <c>{"$class": pythonName}</c>.</summary>
    internal static JsonObject Class(string pythonName) => new() { ["$class"] = pythonName };

    /// <summary>A vector as a JSON array of numbers.</summary>
    internal static JsonArray Vector(IEnumerable<double> values) => new([.. values.Select(Float)]);

    /// <summary>
    /// A point in time as Python's <c>datetime.isoformat()</c> writes it: microseconds appear only
    /// when they are not zero, and the offset is always written as <c>+HH:MM</c>.
    /// </summary>
    internal static JsonObject DateTime(DateTimeOffset value) => new() { ["$dt"] = PyDateTime.FromDateTimeOffset(value).IsoFormat() };

    /// <summary>The result line: <c>json.dumps(result, ensure_ascii=True)</c>, as <c>host.py</c> writes it.</summary>
    internal static string Line(JsonNode result) => JudgeJson.Dumps(result);

    // An int that a long cannot hold is kept as the text of a JSON number, so that no digit is lost.
    private static JsonValue Integer(BigInteger value) =>
        value >= long.MinValue && value <= long.MaxValue
            ? JsonValue.Create((long)value)
            : JsonValue.Create(JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone())!;

    // The Python class names of the C# types that stand for built-in Python types (RULES.md R6).
    private static string ClassName(Type type) =>
        type == typeof(string) ? "str"
        : type == typeof(long) ? "int"
        : type == typeof(double) ? "float"
        : type == typeof(bool) ? "bool"
        : typeof(Exception).IsAssignableFrom(type) ? PythonExceptionName(type) ?? throw NoPythonName(type)
        : type.Name;

    private static JudgeHostErrorException NoPythonName(Type type) =>
        new($"The exception type {type.FullName} has no Python name in Codec.PythonExceptionName.");

    private static JsonNode Float(double value) =>
        double.IsNaN(value) ? new JsonObject { ["$float"] = "nan" }
        : double.IsInfinity(value) ? new JsonObject { ["$float"] = value > 0 ? "inf" : "-inf" }
        : JsonValue.Create(value);

    // {'$set': sorted(items, key=lambda v: json.dumps(v, sort_keys=True))}. The sort is stable, as Python's is.
    private static JsonObject Set(IEnumerable<object?> items, JudgeRegistry registry)
    {
        var encoded = items.Select(item => Encode(item, registry)).ToList();
        var sorted = encoded.OrderBy(node => JudgeJson.Dumps(node, sortKeys: true), StringComparer.Ordinal);
        return new JsonObject { ["$set"] = new JsonArray([.. sorted]) };
    }

    // A dict whose keys are all strings is a JSON object in its order. Any other dict is
    // {'$dict': sorted([[encode(k), encode(v)]], key=lambda p: json.dumps(p[0], sort_keys=True))}.
    private static JsonObject Dict(IEnumerable<(object? Key, object? Value)> entries, JudgeRegistry registry)
    {
        var list = entries.ToList();
        if (list.All(e => e.Key is string))
        {
            var result = new JsonObject();
            foreach (var (key, value) in list)
            {
                result[(string)key!] = Encode(value, registry);
            }

            return result;
        }

        var pairs = list.Select(e => (Key: Encode(e.Key, registry), Value: Encode(e.Value, registry))).ToList();
        var sorted = pairs.OrderBy(p => JudgeJson.Dumps(p.Key, sortKeys: true), StringComparer.Ordinal);
        return new JsonObject { ["$dict"] = new JsonArray([.. sorted.Select(p => (JsonNode)new JsonArray(p.Key, p.Value))]) };
    }

    private static bool IsGenericSet(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ISet<>) || i.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)));
}

/// <summary>A class that a function returns, named as Python names it, for example a response model.</summary>
internal sealed record JudgeClass(string PythonName);

/// <summary>
/// A value that the judge host defines and that encodes as <c>{"$type": PythonName, ...JudgeFields}</c>, for
/// example a fixture type. A product type cannot implement this interface: register it with
/// <see cref="JudgeRegistry.AddType{T}"/>.
/// </summary>
internal interface IJudgeModel
{
    /// <summary>Gets the Python class name.</summary>
    string PythonName { get; }

    /// <summary>Gets the Python field names in field order with their values, not encoded.</summary>
    IEnumerable<KeyValuePair<string, object?>> JudgeFields { get; }
}
