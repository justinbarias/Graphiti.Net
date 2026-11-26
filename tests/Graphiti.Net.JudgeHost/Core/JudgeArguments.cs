namespace Graphiti.JudgeHost.Core;

using System.Collections;
using System.Numerics;
using System.Text.Json;
using Graphiti.PyCompat;

/// <summary>
/// The decoded arguments of one pure case: <c>args</c> as a list and <c>kwargs</c> as a dict with string keys,
/// both in the Python value model. A binding names the Python parameters with <see cref="Signature"/> and reads
/// each one with a typed helper of <see cref="JudgeParameters"/>.
/// </summary>
internal sealed class JudgeArguments
{
    /// <summary>Initializes a new instance of the <see cref="JudgeArguments"/> class.</summary>
    internal JudgeArguments(List<object?> positional, OrderedDictionary<string, object?> keywords)
    {
        Positional = positional;
        Keywords = keywords;
    }

    /// <summary>Gets the positional arguments, decoded.</summary>
    internal List<object?> Positional { get; }

    /// <summary>Gets the keyword arguments, decoded, in their order.</summary>
    internal OrderedDictionary<string, object?> Keywords { get; }

    /// <summary>
    /// Decodes <c>args</c> and <c>kwargs</c> of a case, as <c>run_pure</c> in <c>host.py</c> does. A missing
    /// <c>args</c> is an empty list and a missing <c>kwargs</c> is an empty dict.
    /// </summary>
    internal static JudgeArguments Decode(JsonElement root, ValueDecoder decoder)
    {
        var positional = root.TryGetProperty("args", out var args) ? decoder.Decode(args) : new List<object?>();
        var keywords = root.TryGetProperty("kwargs", out var kwargs) ? decoder.Decode(kwargs) : new OrderedDictionary<string, object?>();
        return new JudgeArguments(
            positional as List<object?> ?? throw new InvalidCastException($"argument after * must be a list, not {Py.TypeName(positional)}"),
            keywords as OrderedDictionary<string, object?> ?? throw new InvalidCastException($"argument after ** must be a mapping, not {Py.TypeName(keywords)}"));
    }

    /// <summary>
    /// Binds the arguments to the Python parameter names in order, as a Python call does. Too many positional
    /// arguments, an unknown keyword, or a keyword that repeats a positional argument is a <c>TypeError</c> in Python,
    /// so it throws <see cref="JudgeTypeRejectedException"/>.
    /// </summary>
    internal JudgeParameters Signature(params string[] parameters)
    {
        if (Positional.Count > parameters.Length)
        {
            throw new JudgeTypeRejectedException($"takes {parameters.Length} positional arguments but {Positional.Count} were given");
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < Positional.Count; index++)
        {
            values[parameters[index]] = Positional[index];
        }

        foreach (var (name, value) in Keywords)
        {
            if (!parameters.Contains(name, StringComparer.Ordinal))
            {
                throw new JudgeTypeRejectedException($"got an unexpected keyword argument '{name}'");
            }

            if (!values.TryAdd(name, value))
            {
                throw new JudgeTypeRejectedException($"got multiple values for argument '{name}'");
            }
        }

        return new JudgeParameters(parameters, values);
    }
}

/// <summary>
/// The arguments of a case bound to the Python parameter names. Each typed read throws
/// <see cref="JudgeTypeRejectedException"/> if the value is not of a type that the C# parameter can hold
/// (RULES.md R22). <see cref="JudgeTypes"/> holds the rules.
/// </summary>
internal sealed class JudgeParameters
{
    private readonly string[] names;
    private readonly Dictionary<string, object?> values;

    /// <summary>Initializes a new instance of the <see cref="JudgeParameters"/> class.</summary>
    internal JudgeParameters(string[] names, Dictionary<string, object?> values)
    {
        this.names = names;
        this.values = values;
    }

    /// <summary>
    /// True if the case gives the parameter. A binding uses it to leave out an argument that the case leaves out,
    /// so that the C# default applies, as the Python default does. It never fills in a default itself.
    /// </summary>
    internal bool Has(string name) => values.ContainsKey(Known(name));

    /// <summary>The decoded value with no type test. A missing argument is a <c>TypeError</c> in Python.</summary>
    internal object? Value(string name) =>
        values.TryGetValue(Known(name), out var value)
            ? value
            : throw new JudgeTypeRejectedException($"missing a required argument: '{name}'");

    /// <summary>The value as <typeparamref name="T"/>. <c>None</c> is accepted only for a <c>Nullable&lt;T&gt;</c> or for <c>object</c>.</summary>
    internal T Get<T>(string name) => (T)JudgeTypes.Convert(typeof(T), Value(name), $"argument '{name}'")!;

    /// <summary>The value as <typeparamref name="T"/>, or null for <c>None</c>: a parameter of a reference type with <c>| None</c>.</summary>
    internal T? GetOrNone<T>(string name)
        where T : class =>
        Value(name) is null ? null : Get<T>(name);

    /// <summary>A <c>str</c> parameter.</summary>
    internal string String(string name) => Get<string>(name);

    /// <summary>A <c>str | None</c> parameter.</summary>
    internal string? NullableString(string name) => GetOrNone<string>(name);

    /// <summary>An <c>int</c> parameter.</summary>
    internal long Long(string name) => Get<long>(name);

    /// <summary>An <c>int | None</c> parameter.</summary>
    internal long? NullableLong(string name) => Get<long?>(name);

    /// <summary>A <c>float</c> parameter. An int is accepted, as Python accepts it.</summary>
    internal double Double(string name) => Get<double>(name);

    /// <summary>A <c>float | None</c> parameter.</summary>
    internal double? NullableDouble(string name) => Get<double?>(name);

    /// <summary>A <c>bool</c> parameter.</summary>
    internal bool Bool(string name) => Get<bool>(name);

    /// <summary>A <c>bool | None</c> parameter.</summary>
    internal bool? NullableBool(string name) => Get<bool?>(name);

    /// <summary>A <c>datetime</c> parameter.</summary>
    internal PyDateTime DateTime(string name) => Get<PyDateTime>(name);

    /// <summary>A <c>datetime | None</c> parameter.</summary>
    internal PyDateTime? NullableDateTime(string name) => Get<PyDateTime?>(name);

    /// <summary>An enum parameter. Only a <c>$enum</c> argument gives a member. Text is a type rejection (R22).</summary>
    internal TEnum Enum<TEnum>(string name)
        where TEnum : struct, Enum => Get<TEnum>(name);

    /// <summary>An enum parameter with <c>| None</c>.</summary>
    internal TEnum? NullableEnum<TEnum>(string name)
        where TEnum : struct, Enum => Get<TEnum?>(name);

    /// <summary>A <c>list[T]</c> parameter. With <c>T</c> = <c>object</c>, the list of the case itself.</summary>
    internal List<T> List<T>(string name) => Get<List<T>>(name);

    /// <summary>A <c>list[T] | None</c> parameter.</summary>
    internal List<T>? NullableList<T>(string name) => GetOrNone<List<T>>(name);

    /// <summary>A <c>dict[str, T]</c> parameter. With <c>T</c> = <c>object</c>, the dict of the case itself.</summary>
    internal OrderedDictionary<string, T> Dict<T>(string name) => Get<OrderedDictionary<string, T>>(name);

    /// <summary>A <c>dict[str, T] | None</c> parameter.</summary>
    internal OrderedDictionary<string, T>? NullableDict<T>(string name) => GetOrNone<OrderedDictionary<string, T>>(name);

    /// <summary>A model parameter: the object that a registered <c>$model</c> factory built.</summary>
    internal T Model<T>(string name)
        where T : class => Get<T>(name);

    /// <summary>A parameter that a registered <c>$ref</c> gives: a constant, a delegate, or a descriptor.</summary>
    internal T Ref<T>(string name)
        where T : class => Get<T>(name);

    private string Known(string name) =>
        names.Contains(name, StringComparer.Ordinal)
            ? name
            : throw new InvalidOperationException($"The binding reads the parameter '{name}', which its signature does not name.");
}

/// <summary>
/// The rules of a typed read (decision D20, RULES.md R22). A value is accepted only if the C# type can hold it
/// and Python would treat it the same way:
/// <list type="bullet">
/// <item><c>None</c> only for <c>Nullable&lt;T&gt;</c>, for <c>object</c>, or through <see cref="JudgeParameters.GetOrNone{T}"/>.</item>
/// <item>A <c>double</c> takes a <c>double</c>, a <c>long</c>, or a <c>BigInteger</c>, converted as <c>float(x)</c> does.</item>
/// <item>A <c>long</c> or an <c>int</c> takes an int that fits. A <c>bool</c>, a <c>double</c>, or a larger int is rejected.</item>
/// <item>A <c>bool</c> takes only a <c>bool</c>. An enum takes only its own member.</item>
/// <item>A <c>List&lt;T&gt;</c> takes a list, and an <c>OrderedDictionary&lt;string, T&gt;</c> a dict with string keys, with each item read by these rules.</item>
/// <item>Each other type takes a value of that runtime type.</item>
/// </list>
/// </summary>
internal static class JudgeTypes
{
    /// <summary>Reads <paramref name="value"/> as <paramref name="target"/>, or throws <see cref="JudgeTypeRejectedException"/>.</summary>
    internal static object? Convert(Type target, object? value, string what)
    {
        var underlying = Nullable.GetUnderlyingType(target);
        if (value is null)
        {
            return underlying is not null || target == typeof(object) ? null : throw Rejected(target, value, what);
        }

        target = underlying ?? target;
        if (target == typeof(object))
        {
            return value;
        }

        if (target == typeof(double))
        {
            return value switch
            {
                double d => d,
                long l => (double)l,
                BigInteger big => Py.IntToFloat(big),
                _ => throw Rejected(target, value, what),
            };
        }

        if (target == typeof(long))
        {
            return value is long ? value : throw Rejected(target, value, what);
        }

        if (target == typeof(int))
        {
            return value is long l && l is >= int.MinValue and <= int.MaxValue ? (int)l : throw Rejected(target, value, what);
        }

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (value is not List<object?> list)
            {
                throw Rejected(target, value, what);
            }

            var item = target.GetGenericArguments()[0];
            if (item == typeof(object))
            {
                return list;
            }

            var result = (IList)Activator.CreateInstance(target, list.Count)!;
            for (var index = 0; index < list.Count; index++)
            {
                result.Add(Convert(item, list[index], $"{what}[{index}]"));
            }

            return result;
        }

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(OrderedDictionary<,>) && target.GetGenericArguments()[0] == typeof(string))
        {
            if (value is not OrderedDictionary<string, object?> dict)
            {
                throw Rejected(target, value, what);
            }

            var item = target.GetGenericArguments()[1];
            if (item == typeof(object))
            {
                return dict;
            }

            var result = (IDictionary)Activator.CreateInstance(target)!;
            foreach (var (key, entry) in dict)
            {
                result.Add(key, Convert(item, entry, $"{what}['{key}']"));
            }

            return result;
        }

        return target.IsInstanceOfType(value) ? value : throw Rejected(target, value, what);
    }

    private static JudgeTypeRejectedException Rejected(Type target, object? value, string what) =>
        new($"{what}: the C# type {target.Name} cannot hold a Python {Py.TypeName(value)}");
}
