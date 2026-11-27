namespace Graphiti.JudgeHost.Core;

using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// Calls <paramref name="method"/> with the arguments of the case and encodes its result. The i-th name of the
    /// signature is the i-th parameter of the method. A last parameter of type <see cref="CancellationToken"/> has no
    /// name and gets <see cref="CancellationToken.None"/>. Each value that the case passes is read with
    /// <see cref="JudgeTypes.Convert"/>, so a wrong type is <see cref="JudgeTypeRejectedException"/>. A
    /// parameter of a reference type that is annotated nullable takes <c>None</c>. A name that the case leaves out
    /// takes the C# default of the parameter, which is the Python default by rule R7, or is a missing argument if the
    /// parameter has none. The result of a <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c>, or
    /// <c>ValueTask&lt;T&gt;</c> is awaited. An exception of the method reaches the caller with its own type and stack.
    /// </summary>
    /// <remarks>
    /// A method group converts to <see cref="Delegate"/> with no cast when the method has one overload and is not
    /// generic: <c>call.Signature("query").InvokeAsync(Helpers.LuceneSanitize)</c>. An overloaded method or a generic
    /// method has no natural delegate type, so it needs a cast to a <c>Func</c> or an <c>Action</c> type, for example
    /// <c>InvokeAsync((Func&lt;string, long, string&gt;)Text.Truncate)</c>. A constructor, a property, or a method that
    /// needs an instance built from an argument uses the explicit reads.
    /// </remarks>
    internal async ValueTask<JsonNode?> InvokeAsync(Delegate method)
    {
        var info = method.Method;
        var parameters = info.GetParameters();
        var count = parameters.Length;
        var token = count > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
        if (token)
        {
            count--;
        }

        if (count != names.Length)
        {
            throw new JudgeHostErrorException($"The signature names {names.Length} parameters, and {info.Name} has {count}.");
        }

        var nullability = new NullabilityInfoContext();
        var arguments = new object?[parameters.Length];
        for (var index = 0; index < count; index++)
        {
            var parameter = parameters[index];
            var name = names[index];
            if (!values.TryGetValue(name, out var value))
            {
                arguments[index] = parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : throw new JudgeTypeRejectedException($"missing a required argument: '{name}'");
                continue;
            }

            var annotation = nullability.Create(parameter);
            arguments[index] = value is null && !parameter.ParameterType.IsValueType && annotation.WriteState == NullabilityState.Nullable
                ? null
                : JudgeTypes.Convert(parameter.ParameterType, value, $"argument '{name}'", annotation);
        }

        if (token)
        {
            arguments[^1] = CancellationToken.None;
        }

        object? result;
        try
        {
            result = info.Invoke(method.Target, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }

        return Codec.Encode(await Await(info.ReturnType, result));
    }

    private static async Task<object?> Await(Type returnType, object? result)
    {
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            result = returnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null);
            returnType = typeof(Task<>).MakeGenericType(returnType.GetGenericArguments());
        }
        else if (returnType == typeof(ValueTask))
        {
            result = ((ValueTask)result!).AsTask();
            returnType = typeof(Task);
        }

        if (returnType == typeof(Task))
        {
            await ((Task)result!).ConfigureAwait(true);
            return null;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var task = (Task)result!;
            await task.ConfigureAwait(true);
            return returnType.GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        return result;
    }

    private string Known(string name) =>
        names.Contains(name, StringComparer.Ordinal)
            ? name
            : throw new JudgeHostErrorException($"The binding reads the parameter '{name}', which its signature does not name.");
}

/// <summary>
/// The rules of a typed read (decision D20, RULES.md R22). A value is accepted only if the C# type can hold it
/// and Python would treat it the same way:
/// <list type="bullet">
/// <item><c>None</c> only for <c>Nullable&lt;T&gt;</c>, for <c>object</c>, or through <see cref="JudgeParameters.GetOrNone{T}"/>.</item>
/// <item>A <c>double</c> takes a <c>double</c>, a <c>long</c>, or a <c>BigInteger</c>, converted as <c>float(x)</c> does.</item>
/// <item>A <c>long</c>, an <c>int</c>, or a <c>ulong</c> takes an int that fits. A <c>bool</c>, a <c>double</c>, or an
/// int out of the range is rejected.</item>
/// <item>A <c>bool</c> takes only a <c>bool</c>. An enum takes only its own member.</item>
/// <item>A <c>List&lt;T&gt;</c>, an <c>IEnumerable&lt;T&gt;</c>, or an <c>IReadOnlyList&lt;T&gt;</c> takes a list, with
/// each item read by these rules. A tuple is not a list.</item>
/// <item>An <c>OrderedDictionary&lt;K, V&gt;</c> or a <c>Dictionary&lt;K, V&gt;</c> takes a dict, with each key and
/// each value read by these rules.</item>
/// <item>A value tuple takes a tuple of the same length, with each item read by these rules.</item>
/// <item>A <c>PySet&lt;T&gt;</c> or a <c>HashSet&lt;T&gt;</c> takes a set, with each item read by these rules. An
/// <c>IReadOnlyCollection&lt;T&gt;</c> takes a list or a set.</item>
/// <item>Each other type takes a value of that runtime type.</item>
/// </list>
/// </summary>
internal static class JudgeTypes
{
    /// <summary>Reads <paramref name="value"/> as <paramref name="target"/>, or throws <see cref="JudgeTypeRejectedException"/>.</summary>
    /// <remarks>
    /// <paramref name="nullability"/> is the annotation of the parameter. With it, a list item or a dict value that the
    /// C# type marks as nullable takes <c>None</c>.
    /// </remarks>
    internal static object? Convert(Type target, object? value, string what, NullabilityInfo? nullability = null)
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

        if (target == typeof(ulong))
        {
            return value switch
            {
                long l when l >= 0 => (ulong)l,
                BigInteger big when big >= 0 && big <= ulong.MaxValue => (ulong)big,
                _ => throw Rejected(target, value, what),
            };
        }

        var definition = target.IsGenericType ? target.GetGenericTypeDefinition() : null;
        if (definition == typeof(IReadOnlyCollection<>) && value is PySet<object?>)
        {
            return Convert(typeof(PySet<>).MakeGenericType(target.GetGenericArguments()[0]), value, what);
        }

        if (definition == typeof(List<>) || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
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

            var itemInfo = nullability is { GenericTypeArguments.Length: 1 } ? nullability.GenericTypeArguments[0] : null;
            var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item), list.Count)!;
            for (var index = 0; index < list.Count; index++)
            {
                result.Add(list[index] is null && !item.IsValueType && itemInfo?.WriteState == NullabilityState.Nullable
                    ? null
                    : Convert(item, list[index], $"{what}[{index}]", itemInfo));
            }

            return result;
        }

        if (definition == typeof(OrderedDictionary<,>) || definition == typeof(Dictionary<,>))
        {
            var keyType = target.GetGenericArguments()[0];
            var item = target.GetGenericArguments()[1];
            if (value is OrderedDictionary<string, object?> dict)
            {
                if (target == typeof(OrderedDictionary<string, object?>))
                {
                    return dict;
                }

                var result = (IDictionary)Activator.CreateInstance(target)!;
                foreach (var (key, entry) in dict)
                {
                    result.Add(Convert(keyType, key, $"{what} key '{key}'")!, Convert(item, entry, $"{what}['{key}']"));
                }

                return result;
            }

            if (value is PyDict open && keyType != typeof(string))
            {
                var result = (IDictionary)Activator.CreateInstance(target)!;
                foreach (var (key, entry) in open)
                {
                    result.Add(Convert(keyType, key, $"{what} key {Py.Repr(key)}")!, Convert(item, entry, $"{what}[{Py.Repr(key)}]"));
                }

                return result;
            }

            throw Rejected(target, value, what);
        }

        if (typeof(ITuple).IsAssignableFrom(target) && target.IsValueType && target.IsGenericType)
        {
            var items = target.GetGenericArguments();
            if (value is not PyTuple tuple || tuple.Count != items.Length || items.Length > 7)
            {
                throw Rejected(target, value, what);
            }

            var converted = new object?[items.Length];
            for (var index = 0; index < items.Length; index++)
            {
                converted[index] = Convert(items[index], tuple[index], $"{what}[{index}]");
            }

            return Activator.CreateInstance(target, converted);
        }

        if (definition == typeof(PySet<>) || definition == typeof(HashSet<>))
        {
            if (value is not PySet<object?> set)
            {
                throw Rejected(target, value, what);
            }

            var item = target.GetGenericArguments()[0];
            if (target == typeof(PySet<object?>))
            {
                return set;
            }

            var read = typeof(JudgeTypes).GetMethod(definition == typeof(PySet<>) ? nameof(ToPySet) : nameof(ToHashSet), BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                return read.MakeGenericMethod(item).Invoke(null, [set, what]);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        return target.IsInstanceOfType(value) ? value : throw Rejected(target, value, what);
    }

    private static PySet<T> ToPySet<T>(PySet<object?> set, string what) =>
        new(set.Select((item, index) => (T)Convert(typeof(T), item, $"{what} item {index}")!));

    private static HashSet<T> ToHashSet<T>(PySet<object?> set, string what) =>
        [.. set.Select((item, index) => (T)Convert(typeof(T), item, $"{what} item {index}")!)];

    private static JudgeTypeRejectedException Rejected(Type target, object? value, string what) =>
        new($"{what}: the C# type {target.Name} cannot hold a Python {Py.TypeName(value)}");
}
