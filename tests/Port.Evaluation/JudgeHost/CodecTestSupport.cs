namespace Port.Evaluation.JudgeHost;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The enum of port/tools/record_codec_values.py.</summary>
internal enum Color
{
    [PyEnumMember("RED", "red")]
    Red,

    [PyEnumMember("ONE", 1)]
    One,

    [PyEnumMember("BIG", long.MinValue)]
    Big,
}

/// <summary>A model in a test: a <c>$type</c> result or a test model of the recorder.</summary>
internal sealed record TestModel(string PythonName, OrderedDictionary<string, object?> Fields) : IJudgeModel
{
    public IEnumerable<KeyValuePair<string, object?>> JudgeFields => Fields;
}

/// <summary>A <c>{"$exc": name}</c> result in a golden file.</summary>
internal sealed record ExceptionResult(string PythonName);

/// <summary>A <c>{"$unknown": name}</c> result in a golden file.</summary>
internal sealed record UnknownResult(string PythonName);

/// <summary>An object of a class that the codec does not know. Its C# name is the Python name.</summary>
internal sealed class Opaque;

/// <summary>A <c>$ref</c>, <c>$enum</c>, or <c>$model</c> of a case, resolved to a placeholder.</summary>
internal sealed record CaseValue(string Tag, string Path, object? Data);

/// <summary>
/// A decoder for the tests. It knows the result tags (<c>$date</c>, <c>$type</c>, <c>$class</c>, <c>$exc</c>,
/// <c>$unknown</c>) and the <c>test:</c> paths of the recorder. With <c>anyPath</c>, each other path and each flow
/// tag gives a placeholder, so that the whole argument is decoded. A list as a dict key or a set item becomes a
/// tuple, because a result carries a tuple as a list.
/// </summary>
internal sealed class TestDecoder(bool anyPath = false) : ValueDecoder(NewRegistry())
{
    internal static readonly Dictionary<string, Type> ExceptionTypes = new(StringComparer.Ordinal)
    {
        ["ValueError"] = typeof(ArgumentException),
        ["KeyError"] = typeof(KeyNotFoundException),
        ["TypeError"] = typeof(InvalidCastException),
        ["ZeroDivisionError"] = typeof(DivideByZeroException),
        ["AttributeError"] = typeof(MissingMemberException),
        ["NotImplementedError"] = typeof(NotImplementedException),
        ["OverflowError"] = typeof(OverflowException),
        ["RuntimeError"] = typeof(InvalidOperationException),
        ["IndexError"] = typeof(ArgumentOutOfRangeException),
    };

    internal static JudgeRegistry NewRegistry() => new JudgeRegistry()
        .AddEncoder<ExceptionResult>(e => new JsonObject { ["$exc"] = e.PythonName })
        .AddEncoder<UnknownResult>(u => new JsonObject { ["$unknown"] = u.PythonName });

    /// <summary>Parses one JSON text and decodes it.</summary>
    internal object? DecodeText(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Decode(document.RootElement);
    }

    protected override bool TryDecodeOther(JsonElement value, out object? result)
    {
        if (Find(value, "$date", out var date))
        {
            result = DateOnly.ParseExact(date.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        if (Find(value, "$type", out var type))
        {
            var fields = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (JudgeJson.ReadName(property) != "$type")
                {
                    fields[JudgeJson.ReadName(property)] = Decode(property.Value);
                }
            }

            result = new TestModel(type.GetString()!, fields);
            return true;
        }

        if (Find(value, "$class", out var name))
        {
            result = new JudgeClass(name.GetString()!);
            return true;
        }

        if (Find(value, "$exc", out var exc))
        {
            result = new ExceptionResult(exc.GetString()!);
            return true;
        }

        if (Find(value, "$unknown", out var unknown))
        {
            result = new UnknownResult(unknown.GetString()!);
            return true;
        }

        if (anyPath)
        {
            foreach (var tag in FlowTags)
            {
                if (Find(value, tag, out var raw))
                {
                    result = new CaseValue(tag, string.Empty, raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.GetRawText());
                    return true;
                }
            }
        }

        result = null;
        return false;
    }

    protected override object? ResolveRef(string path)
    {
        var (prefix, name) = Split(path);
        return prefix switch
        {
            "test:class" when ExceptionTypes.TryGetValue(name, out var type) => type,
            "test:class" => new JudgeClass(name),
            "test:exc" => (Exception)Activator.CreateInstance(ExceptionTypes[name])!,
            "test:unknown" => new Opaque(),
            _ when anyPath => new CaseValue("$ref", path, null),
            _ => base.ResolveRef(path),
        };
    }

    protected override object? ResolveEnum(string path, object? value) => path switch
    {
        "test:Color" => PyEnum.FromValue<Color>(value),
        _ when anyPath => new CaseValue("$enum", path, value),
        _ => base.ResolveEnum(path, value),
    };

    protected override object? ResolveModel(string path, OrderedDictionary<string, object?> data) => path switch
    {
        "test:Point" => new TestModel("Point", data),
        "test:Box" => new TestModel("Box", data),
        _ when anyPath => new CaseValue("$model", path, data),
        _ => base.ResolveModel(path, data),
    };

    protected override object? DecodeKey(JsonElement value) => Hashable(Decode(value));

    private static object? Hashable(object? value) =>
        value is List<object?> list ? PyTuple.From(list.Select(Hashable)) : value;

    private static (string Prefix, string Name) Split(string path)
    {
        var cut = path.LastIndexOf(':');
        return (path[..cut], path[(cut + 1)..]);
    }
}

/// <summary>Paths in the repository, found from the test folder.</summary>
internal static class Repository
{
    internal static string Root { get; } = FindRoot();

    internal static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Graphiti.Net.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("No Graphiti.Net.slnx above the test folder");
    }
}
