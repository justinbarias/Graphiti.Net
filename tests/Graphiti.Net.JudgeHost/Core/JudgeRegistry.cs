namespace Graphiti.JudgeHost.Core;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Graphiti.PyCompat;

/// <summary>
/// The tables that a binding file fills, because the codec cannot know product types.
/// Decoding: a <c>$ref</c> path gives an object or a delegate, a <c>$enum</c> path gives an enum type, and a
/// <c>$model</c> path gives a factory that takes the decoded <c>data</c>. A path with no entry throws
/// <see cref="JudgeNotBoundException"/>, because the unit that owns it is not ported yet.
/// Encoding: a product type gives a delegate that encodes it, so that a nested model encodes itself.
/// Each method returns the registry, so that a binding file can chain the calls.
/// </summary>
internal sealed class JudgeRegistry
{
    private readonly Dictionary<string, object?> refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<object?, object>> enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<OrderedDictionary<string, object?>, object?>> models = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Func<object, JsonNode?>> encoders = [];
    private readonly ConcurrentDictionary<Type, Func<object, JsonNode?>?> found = new();

    /// <summary><c>{"$ref": path}</c> gives <paramref name="value"/>: a constant, a delegate, or a type descriptor.</summary>
    internal JudgeRegistry AddRef(string path, object? value)
    {
        refs.Add(path, value);
        return this;
    }

    /// <summary><c>{"$enum": path, "value": v}</c> gives <c>PyEnum.FromValue&lt;T&gt;(v)</c>.</summary>
    internal JudgeRegistry AddEnum<T>(string path)
        where T : struct, Enum
    {
        enums.Add(path, value => PyEnum.FromValue<T>(value));
        return this;
    }

    /// <summary>
    /// <c>{"$model": path, "data": {...}}</c> gives <c>factory(data)</c>. For a model class the factory is
    /// <c>M.ModelValidate</c> (RULES.md R7). For a function it calls the function with the keyword arguments.
    /// </summary>
    internal JudgeRegistry AddModel(string path, Func<OrderedDictionary<string, object?>, object?> factory)
    {
        models.Add(path, factory);
        return this;
    }

    /// <summary>A value of type <typeparamref name="T"/>, or of a type derived from it, encodes with <paramref name="encode"/>.</summary>
    internal JudgeRegistry AddEncoder<T>(Func<T, JsonNode?> encode)
        where T : notnull
    {
        encoders.Add(typeof(T), value => encode((T)value));
        found.Clear();
        return this;
    }

    /// <summary>
    /// A model or a dataclass encodes as <c>{"$type": pythonName, ...fields}</c>. <paramref name="fields"/> gives the
    /// Python field names in field order with the attribute values, not the dumped values: a nested model stays a
    /// model, so that it encodes as <c>$type</c>, as <c>codec.py</c> does with <c>getattr</c>.
    /// </summary>
    internal JudgeRegistry AddType<T>(string pythonName, Func<T, IEnumerable<KeyValuePair<string, object?>>> fields)
        where T : notnull =>
        AddEncoder<T>(value => Codec.Model(pythonName, fields(value), this));

    /// <summary>The object at a <c>$ref</c> path.</summary>
    internal object? ResolveRef(string path) =>
        refs.TryGetValue(path, out var value) ? value : throw NotBound("$ref", path);

    /// <summary>The member of the enum at a <c>$enum</c> path whose value is <paramref name="value"/>.</summary>
    internal object ResolveEnum(string path, object? value) =>
        enums.TryGetValue(path, out var fromValue) ? fromValue(value) : throw NotBound("$enum", path);

    /// <summary>The result of the factory at a <c>$model</c> path.</summary>
    internal object? ResolveModel(string path, OrderedDictionary<string, object?> data) =>
        models.TryGetValue(path, out var factory) ? factory(data) : throw NotBound("$model", path);

    /// <summary>The encoder for a runtime type: its own entry, else the nearest base type, else an interface.</summary>
    internal Func<object, JsonNode?>? FindEncoder(Type type)
    {
        if (encoders.Count == 0)
        {
            return null;
        }

        return found.GetOrAdd(type, t =>
        {
            for (var current = t; current is not null; current = current.BaseType)
            {
                if (encoders.TryGetValue(current, out var encode))
                {
                    return encode;
                }
            }

            foreach (var face in t.GetInterfaces())
            {
                if (encoders.TryGetValue(face, out var encode))
                {
                    return encode;
                }
            }

            return null;
        });
    }

    private static JudgeNotBoundException NotBound(string tag, string path) =>
        new(tag, path, $"No binding file registered the {tag} path '{path}'. Its unit is not ported yet.");
}
