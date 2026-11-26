namespace Graphiti.PyCompat;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>
/// The field readers of pydantic lax mode at the pinned version (pydantic 2.12.5, pydantic-core 2.41.5), and the
/// wrapping of a validator's <c>ValueError</c> (RULES.md R7). <c>ModelValidate</c> of a ported model reads each field
/// with <see cref="PydanticFields"/> and one of these readers, so that no model writes a coercion by hand.
/// </summary>
/// <remarks>
/// A reader takes the open value of the field (RULES.md R6) and returns the typed value, or throws
/// <see cref="ValidationException"/> with the errors at a location relative to the value. A container reader puts
/// the index or the key in front of the location of each item error, and reads all items before it throws, as
/// pydantic does. <see cref="PydanticFields"/> puts the field name in front.
/// An <c>int</c> that a C# <see cref="long"/> cannot hold throws <see cref="OverflowException"/> where pydantic
/// returns the int (the limit of RULES.md R4, debt T25).
/// </remarks>
internal static partial class Pydantic
{
    private const string StringType = "Input should be a valid string";
    private const string StringUnicode = "Input should be a valid string, unable to parse raw data as a unicode string";
    private const string IntType = "Input should be a valid integer";
    private const string IntParsing = "Input should be a valid integer, unable to parse string as an integer";
    private const string IntParsingSize = "Unable to parse input string as an integer, exceeded maximum size";
    private const string FloatType = "Input should be a valid number";
    private const string FloatParsing = "Input should be a valid number, unable to parse string as a number";
    private const string BoolType = "Input should be a valid boolean";
    private const string BoolParsing = "Input should be a valid boolean, unable to interpret input";
    private const double TwoPow63 = 9223372036854775808.0;

    /// <summary>A <c>str</c> field: a text, UTF-8 <c>bytes</c>, or an enum member with a text value.</summary>
    internal static string Str(object? value) => value switch
    {
        string s => s,
        byte[] b => DecodeStrict(b) ?? throw Error("string_unicode", StringUnicode),
        System.Enum e when PyEnum.Value(e) is string s => s,
        _ => throw Error("string_type", StringType),
    };

    /// <summary>
    /// An <c>int</c> field: an int, a bool, a float with no fraction, or a text or <c>bytes</c> of an int, with
    /// spaces around it, <c>_</c> between digits, and a fraction of zeros (<c>" 1_000.00 "</c>).
    /// </summary>
    /// <remarks>
    /// For an enum member, pydantic returns <c>member.value</c> without a check, also a text value in an
    /// <c>int</c> field. A <see cref="long"/> cannot hold a text, so a member with a text value throws
    /// <see cref="InvalidCastException"/>. No upstream model gets an enum member in an <c>int</c> field.
    /// </remarks>
    internal static long Int(object? value) => value switch
    {
        long l => l,
        bool b => b ? 1 : 0,
        BigInteger => throw TooBig(),
        double d => IntOfFloat(d),
        string s => IntOfText(EncodeStrict(s) is null ? throw Error("string_unicode", StringUnicode) : s),
        byte[] b => IntOfText(DecodeStrict(b) ?? throw Error("int_parsing", IntParsing)),
        System.Enum e => PyEnum.Value(e) is long l ? l : throw new InvalidCastException(
            $"pydantic returns {Py.Repr(PyEnum.Value(e))} for an int field, which a long cannot hold"),
        _ => throw Error("int_type", IntType),
    };

    /// <summary>A <c>float</c> field: a float, an int, a bool, or a text or <c>bytes</c> of a number (Rust's grammar, <c>inf</c> and <c>nan</c>, <c>_</c> between digits).</summary>
    internal static double Float(object? value) => value switch
    {
        double d => d,
        long l => l,
        bool b => b ? 1 : 0,
        BigInteger big => Py.IntToFloat(big),
        string s => FloatOfText(EncodeStrict(s) is null ? throw Error("string_unicode", StringUnicode) : s),
        byte[] b => FloatOfText(DecodeStrict(b) ?? throw Error("float_parsing", FloatParsing)),
        _ => throw Error("float_type", FloatType),
    };

    /// <summary>
    /// A <c>bool</c> field: a bool, the int 0 or 1, a float 0.0 or 1.0, or one of the texts <c>0 off f false n no
    /// 1 on t true y yes</c> in any ASCII case, as text or <c>bytes</c>.
    /// </summary>
    internal static bool Bool(object? value)
    {
        switch (value)
        {
            case bool b:
                return b;
            case long l:
                return l switch { 0 => false, 1 => true, _ => throw Error("bool_parsing", BoolParsing) };
            case double d when double.IsFinite(d) && Math.Truncate(d) == d && Math.Abs(d) < TwoPow63:
                return d switch { 0 => false, 1 => true, _ => throw Error("bool_parsing", BoolParsing) };
            case string s:
                return BoolOfText(EncodeStrict(s) is null ? throw Error("string_unicode", StringUnicode) : s);
            case byte[] bytes:
                return BoolOfText(DecodeStrict(bytes) ?? throw Error("bool_parsing", BoolParsing));
            default:
                throw Error("bool_type", BoolType);
        }
    }

    /// <summary>An <c>Any</c> field: the value itself, not copied.</summary>
    internal static object? Any(object? value) => value;

    /// <summary>
    /// A field of a plain <c>Enum</c> class: a member of <typeparamref name="T"/>, or a value that equals the value
    /// of a member. A text with a lone surrogate gives the <c>string_unicode</c> error, as pydantic does.
    /// </summary>
    internal static T Enum<T>(object? value)
        where T : struct, Enum
    {
        if (value is T member)
        {
            return member;
        }

        if (value is string s && EncodeStrict(s) is null)
        {
            throw Error("string_unicode", StringUnicode);
        }

        foreach (var m in System.Enum.GetValues<T>())
        {
            // PORT: every upstream enum of a model field has text values. The rule for an enum with int values
            // (True, 1.0, or "1" for the value 1) is not recorded, so only a value that equals a member value matches.
            if (value is string or long && PyValueEquality.Equal(PyEnum.Value(m), value))
            {
                return m;
            }
        }

        throw Error("enum", "Input should be " + OrList(System.Enum.GetValues<T>().Select(m => Py.Repr(PyEnum.Value(m)))));
    }

    /// <summary>A <c>Literal['a', 'b']</c> field of texts: one of the texts, exactly. RULES.md R7 keeps the field a <see cref="string"/>.</summary>
    internal static string Literal(object? value, params string[] allowed)
    {
        if (value is string s)
        {
            if (EncodeStrict(s) is null)
            {
                throw Error("string_unicode", StringUnicode);
            }

            if (Array.IndexOf(allowed, s) >= 0)
            {
                return s;
            }
        }

        throw Error("literal_error", "Input should be " + OrList(allowed.Select(a => Py.Repr(a))));
    }

    /// <summary>
    /// A <c>str | int | float</c> field in pydantic's smart mode: a text, an int, or a float stays as it is. Else
    /// the first of <c>str</c>, <c>int</c>, <c>float</c> in lax mode that accepts the value wins, so <c>True</c>
    /// gives the int 1 and <c>bytes</c> give a text. The result is a <see cref="string"/>, <see cref="long"/>, or
    /// <see cref="double"/>. If no member accepts the value, the errors of all three are reported.
    /// </summary>
    internal static object StrIntFloat(object? value)
    {
        switch (value)
        {
            case string s:
                return s;
            case long l:
                return l;
            case BigInteger:
                throw TooBig();
            case double d:
                return d;
        }

        var errors = new List<PydanticError>();
        foreach (var (name, read) in new (string, Func<object?, object>)[] { ("str", v => Str(v)), ("int", v => Int(v)), ("float", v => Float(v)) })
        {
            try
            {
                return read(value);
            }
            catch (ValidationException e)
            {
                errors.AddRange(e.Errors.Select(error => error.Under(name)));
            }
        }

        throw new ValidationException(null, errors);
    }

    /// <summary>A <c>Callable</c> field with <c>arbitrary_types_allowed</c>: a delegate of type <typeparamref name="T"/>.</summary>
    internal static T Callable<T>(object? value)
        where T : Delegate => value as T ?? throw Error("callable_type", "Input should be callable");

    /// <summary>
    /// A <c>T | None</c> field for a reference type: <c>None</c> gives null, and each other value goes to
    /// <paramref name="read"/>. A field with a default is still not optional unless its type has <c>| None</c>.
    /// </summary>
    internal static T? Optional<T>(object? value, Func<object?, T> read)
        where T : class => value is null ? null : read(value);

    /// <summary>A <c>T | None</c> field for a value type, such as <c>int | None</c> or <c>datetime | None</c>.</summary>
    internal static T? OptionalValue<T>(object? value, Func<object?, T> read)
        where T : struct => value is null ? null : read(value);

    /// <summary>
    /// A <c>list[T]</c> field: a list, a tuple, a set, or a frozenset, read item by item into a new list.
    /// A text, <c>bytes</c>, or a dict is not a list.
    /// </summary>
    internal static List<T> List<T>(object? value, Func<object?, T> item)
    {
        IEnumerable? items = value switch
        {
            string or byte[] or IDictionary => null,
            IList or PyTuple or IPySet => (IEnumerable)value,
            _ => null,
        };
        if (items is null)
        {
            throw Error("list_type", "Input should be a valid list");
        }

        var result = new List<T>();
        var errors = new List<PydanticError>();
        var i = 0;
        foreach (var x in items)
        {
            try
            {
                result.Add(item(x));
            }
            catch (ValidationException e)
            {
                errors.AddRange(e.Errors.Select(error => error.Under(i)));
            }

            i++;
        }

        return errors.Count > 0 ? throw new ValidationException(null, errors) : result;
    }

    /// <summary>
    /// A <c>dict[str, T]</c> field: a dict read entry by entry into a new dict in the same order. A key that is not
    /// a text gives the error at <c>(key, '[key]')</c>.
    /// </summary>
    internal static OrderedDictionary<string, T> Dict<T>(object? value, Func<object?, T> item)
    {
        if (value is not (PyDict or IDictionary))
        {
            throw Error("dict_type", "Input should be a valid dictionary");
        }

        var result = new OrderedDictionary<string, T>();
        var errors = new List<PydanticError>();
        foreach (var (key, x) in Py.DictEntries(value))
        {
            if (key is not string k)
            {
                errors.Add(new PydanticError([KeyLoc(key), "[key]"], "string_type", StringType));
                k = string.Empty;
            }

            try
            {
                var v = item(x);
                if (key is string)
                {
                    result[k] = v;
                }
            }
            catch (ValidationException e)
            {
                errors.AddRange(e.Errors.Select(error => error.Under(KeyLoc(key))));
            }
        }

        return errors.Count > 0 ? throw new ValidationException(null, errors) : result;
    }

    /// <summary>
    /// A field whose type is a model <typeparamref name="T"/>: an instance of <typeparamref name="T"/> as it is, or
    /// a dict that <paramref name="modelValidate"/> reads (<c>T.ModelValidate</c>). <paramref name="title"/> is the
    /// Python class name for the error.
    /// </summary>
    internal static T Model<T>(object? value, string title, Func<OrderedDictionary<string, object?>, T> modelValidate)
        where T : class => value switch
        {
            T model => model,
            OrderedDictionary<string, object?> data => modelValidate(data),
            PyDict pd => modelValidate(StringKeys(pd)),
            _ => throw Error("model_type", $"Input should be a valid dictionary or instance of {title}"),
        };

    /// <summary>
    /// A discriminated union, <c>Annotated[A | B, Field(discriminator='op')]</c> (RULES.md R7): the dict key
    /// <paramref name="discriminator"/> names the case, and the <c>ModelValidate</c> of that case reads the dict.
    /// An instance of <typeparamref name="T"/> stays as it is. Errors of the case are under the tag.
    /// </summary>
    internal static T Tagged<T>(object? value, string discriminator, params (string Tag, Func<OrderedDictionary<string, object?>, T> ModelValidate)[] cases)
        where T : class
    {
        if (value is T model)
        {
            return model;
        }

        var data = value switch
        {
            OrderedDictionary<string, object?> d => d,
            PyDict pd => StringKeys(pd),
            _ => throw Error("model_attributes_type", "Input should be a valid dictionary or object to extract fields from"),
        };
        if (!data.TryGetValue(discriminator, out var tag))
        {
            throw Error("union_tag_not_found", $"Unable to extract tag using discriminator {Py.Repr(discriminator)}");
        }

        foreach (var (name, modelValidate) in cases)
        {
            if (tag is string t && t == name)
            {
                try
                {
                    return modelValidate(data);
                }
                catch (ValidationException e)
                {
                    throw new ValidationException(null, [.. e.Errors.Select(error => error.Under(name))]);
                }
            }
        }

        throw Error(
            "union_tag_invalid",
            $"Input tag '{Py.Str(tag)}' found using {Py.Repr(discriminator)} does not match any of the expected tags: {string.Join(", ", cases.Select(c => Py.Repr(c.Tag)))}");
    }

    /// <summary>
    /// Runs the code of a <c>@field_validator</c> or <c>@model_validator</c> (RULES.md R7). pydantic turns a
    /// <c>ValueError</c> that a validator raises into a <c>ValidationError</c>: this catches an
    /// <see cref="ArgumentException"/> and throws <see cref="ValidationException"/> with the caught exception inside.
    /// A <see cref="ValidationException"/> from the validator passes through unchanged.
    /// </summary>
    /// <param name="validator">The validator code.</param>
    /// <param name="title">The model name for the message, as pydantic gives it. Optional.</param>
    /// <param name="field">The field of a field validator, the location of pydantic's error. Null for a model validator.</param>
    internal static void Wrap(Action validator, string? title = null, string? field = null) =>
        Wrap<ArgumentException>(validator, title, field);

    /// <summary>
    /// <see cref="Wrap(Action, string?, string?)"/> that also catches <typeparamref name="TException"/>, for a
    /// Python exception that is a <c>ValueError</c> and has a C# class that does not derive from
    /// <see cref="ArgumentException"/>, such as <c>NodeLabelValidationException</c>.
    /// </summary>
    internal static void Wrap<TException>(Action validator, string? title = null, string? field = null)
        where TException : Exception
    {
        try
        {
            validator();
        }
        catch (Exception e) when (e is not ValidationException && e is ArgumentException or TException)
        {
            throw new ValidationException(title, [new PydanticError(field is null ? [] : [field], "value_error", "Value error, " + e.Message)], e);
        }
    }

    /// <summary>
    /// <c>model_dump()</c> of one value: a list (also a typed list) becomes a new <c>List&lt;object?&gt;</c>, a dict
    /// with text keys a new <c>OrderedDictionary&lt;string, object?&gt;</c>, a tuple a new tuple, and a model its
    /// <c>ModelDump</c>, each item dumped the same way. Other values stay as they are.
    /// </summary>
    internal static object? Dump(object? value) => value switch
    {
        null or bool or long or BigInteger or double or string or byte[] or PyDateTime or DateOnly or System.Enum or IPySet => value,
        IPydanticModel model => model.ModelDump(),
        PyTuple t => PyTuple.From(t.Select(Dump)),
        PyDict pd => DumpPyDict(pd),
        IDictionary d => DumpDict(d),
        IList l => l.Cast<object?>().Select(Dump).ToList(),
        _ => throw new InvalidOperationException($"Pydantic.Dump: {value.GetType().FullName} is outside the Python value model (RULES.md R6)"),
    };

    /// <summary>
    /// The body of <c>ModelDump</c>: a dict of the Python field names in field order, each value passed through
    /// <see cref="Dump(object?)"/>, or through <see cref="JsonValue(object?)"/> with <paramref name="json"/>.
    /// </summary>
    /// <remarks>
    /// A known difference: pydantic does not validate a default, so a float field with an int default keeps the int
    /// until a value is assigned. The one upstream site is <c>SearchConfig.reranker_min_score = Field(default=0)</c>,
    /// which Python dumps as <c>0</c> and the port's <see cref="double"/> property as <c>0.0</c>. The judge compares
    /// numbers with a tolerance. A text made from the dump (<see cref="DumpJson(object?)"/>, a prompt) differs.
    /// </remarks>
    internal static OrderedDictionary<string, object?> DumpFields(bool json, params (string Name, object? Value)[] fields)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (name, value) in fields)
        {
            result[name] = json ? JsonValue(value) : Dump(value);
        }

        return result;
    }

    /// <summary>
    /// What <c>model_dump(mode='json')</c> gives for a value: an enum member becomes its value, a datetime the text of
    /// <see cref="JsonDateTime(PyDateTime)"/>, a date <c>YYYY-MM-DD</c>, <c>bytes</c> UTF-8 text, a float
    /// <c>nan</c> or <c>inf</c> <c>None</c>, a tuple or a set a list, and a dict key a text. A model gives its
    /// <c>ModelDump(json: true)</c>. <c>bytes</c> that are not UTF-8 throw <see cref="DecoderFallbackException"/>
    /// (<c>UnicodeDecodeError</c>).
    /// </summary>
    internal static object? JsonValue(object? value) => value switch
    {
        null or bool or long or BigInteger or string => value,
        double d => double.IsFinite(d) ? d : null,
        PyDateTime dt => JsonDateTime(dt),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] b => JsonText(b),
        System.Enum e => JsonValue(PyEnum.Value(e)),
        IPydanticModel model => model.ModelDump(json: true),
        PyDict or IDictionary => JsonDict(value),
        PyTuple or IPySet or IList => ((IEnumerable)value).Cast<object?>().Select(JsonValue).ToList(),
        _ => throw new InvalidOperationException($"Pydantic.JsonValue: {value.GetType().FullName} is outside the Python value model (RULES.md R6)"),
    };

    /// <summary>
    /// pydantic's JSON text of a datetime: <c>YYYY-MM-DDTHH:MM:SS</c>, then <c>.ffffff</c> if the microseconds are
    /// not zero, then <c>Z</c> for UTC or <c>±HH:MM</c> (the seconds of an offset are dropped), and nothing for a
    /// naive value. It differs from <c>isoformat()</c>, which writes <c>+00:00</c>.
    /// </summary>
    internal static string JsonDateTime(PyDateTime dt)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{dt.Year:D4}-{dt.Month:D2}-{dt.Day:D2}T{dt.Hour:D2}:{dt.Minute:D2}:{dt.Second:D2}");
        if (dt.Microsecond != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $".{dt.Microsecond:D6}");
        }

        if (dt.OffsetMicroseconds is { } offset)
        {
            if (offset == 0)
            {
                sb.Append('Z');
            }
            else
            {
                var minutes = Math.Abs(offset) / 60_000_000;
                sb.Append(offset < 0 ? '-' : '+').Append(CultureInfo.InvariantCulture, $"{minutes / 60:D2}:{minutes % 60:D2}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// pydantic's text of <c>bytes</c> in JSON mode: strict UTF-8. Its <c>UnicodeDecodeError</c> names the first
    /// byte that is not valid and the reason <c>invalid utf-8</c>, which differs from <c>bytes.decode()</c>.
    /// </summary>
    private static string JsonText(byte[] b)
    {
        var chars = new char[b.Length];
        if (System.Text.Unicode.Utf8.ToUtf16(b, chars, out var read, out var written, replaceInvalidSequences: false) == System.Buffers.OperationStatus.Done)
        {
            return new string(chars, 0, written);
        }

        throw new DecoderFallbackException($"'utf-8' codec can't decode byte 0x{b[read]:x2} in position {read}: invalid utf-8");
    }

    private static OrderedDictionary<string, object?> JsonDict(object dict)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in Py.DictEntries(dict))
        {
            result[JsonKey(key)] = JsonValue(value);
        }

        return result;
    }

    /// <summary>pydantic's text of a dict key in JSON mode.</summary>
    private static string JsonKey(object? key) => key switch
    {
        string s => s,
        null => "None",
        bool b => b ? "true" : "false",
        long or BigInteger => Py.Str(key),
        double d => double.IsFinite(d) ? Py.Repr(d) : "None",
        PyDateTime dt => JsonDateTime(dt),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] b => JsonText(b),
        System.Enum e => JsonKey(PyEnum.Value(e)),
        PyTuple t => string.Join(",", t.Select(JsonKey)),
        _ => throw new InvalidOperationException($"Pydantic.JsonValue: a dict key of type {key.GetType().FullName}"),
    };

    private static PyDict DumpPyDict(PyDict pd)
    {
        var result = new PyDict();
        foreach (var (key, value) in pd)
        {
            result[key] = Dump(value);
        }

        return result;
    }

    private static OrderedDictionary<string, object?> DumpDict(IDictionary d)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (DictionaryEntry entry in d)
        {
            result[entry.Key as string ?? throw new InvalidOperationException("Pydantic.Dump: a dict key that is not a text needs a PyDict")] = Dump(entry.Value);
        }

        return result;
    }

    private static OrderedDictionary<string, object?> StringKeys(PyDict pd)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in pd)
        {
            if (key is string k)
            {
                result[k] = value;
            }
        }

        return result;
    }

    private static object KeyLoc(object? key) => key switch
    {
        string s => s,
        long l => (object)l,
        _ => Py.Repr(key),
    };

    /// <summary>pydantic's list of choices: <c>'a'</c>, <c>'a' or 'b'</c>, <c>'a', 'b' or 'c'</c>.</summary>
    private static string OrList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 1 ? list[0] : string.Join(", ", list.Take(list.Count - 1)) + " or " + list[^1];
    }

    private static ValidationException Error(string type, string msg) => ValidationException.One(type, msg);

    private static OverflowException TooBig() => new("Python int too large to convert to C long");

    private static string? DecodeStrict(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static byte[]? EncodeStrict(string s)
    {
        try
        {
            return PyText.EncodeUtf8(s);
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }
}

/// <summary>A ported model, for <see cref="Pydantic.Dump(object?)"/> of a nested model.</summary>
internal interface IPydanticModel
{
    /// <summary><c>model_dump()</c>, or <c>model_dump(mode='json')</c> with <paramref name="json"/>.</summary>
    OrderedDictionary<string, object?> ModelDump(bool json = false);
}
