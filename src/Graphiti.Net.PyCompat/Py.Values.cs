namespace Graphiti.PyCompat;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>
/// The helpers of the Python value model (RULES.md R6, decision D21): <c>repr</c>, <c>str</c>, <c>bool()</c>,
/// <c>len</c>, <c>dict.get</c>, <c>dict.setdefault</c>, unpacking, <c>copy.deepcopy</c>, <c>enum.value</c>,
/// and typed reads. Each one accepts the closed set of R6 and throws <see cref="InvalidOperationException"/>
/// for a runtime type outside it. A list is any <see cref="IList"/> and a dict with string keys is any
/// <see cref="IDictionary"/>, so typed data such as <c>List&lt;string&gt;</c> prints as Python prints it.
/// </summary>
internal static partial class Py
{
    /// <summary>
    /// <c>repr(x)</c>. A list or a dict that holds itself prints <c>[...]</c> or <c>{...}</c>, as Python does.
    /// </summary>
    internal static string Repr(object? x)
    {
        var sb = new StringBuilder();
        WriteRepr(sb, x, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return sb.ToString();
    }

    /// <summary>
    /// <c>str(x)</c>, also <c>f'{x}'</c>. A string is itself, a date is <c>YYYY-MM-DD</c>, an enum member
    /// is <c>Class.name</c>, and each other value is its <c>repr</c>.
    /// </summary>
    internal static string Str(object? x) => x switch
    {
        string s => s,
        PyDateTime dt => dt.Str(),
        DateOnly d => DateIso(d),
        Enum e => PyEnum.ClassName(e) + "." + PyEnum.Name(e),
        _ => Repr(x),
    };

    /// <summary><c>bool(x)</c>: false for <c>None</c>, zero (also <c>-0.0</c>), and each empty text or container.</summary>
    internal static bool Truthy(object? x) => x switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        BigInteger big => !big.IsZero,
        double d => d != 0,
        string s => s.Length > 0,
        byte[] bytes => bytes.Length > 0,
        PyTuple t => t.Count > 0,
        IPySet set => set.Count > 0,
        PyDict pd => pd.Count > 0,
        IDictionary or IList => ((ICollection)x).Count > 0,
        DateOnly or PyDateTime or Enum => true,
        _ => throw Outside(x, nameof(Truthy)),
    };

    /// <summary>
    /// <c>len(x)</c>. Text counts code points. A value with no length throws
    /// <see cref="InvalidCastException"/> (<c>TypeError: object of type '...' has no len()</c>).
    /// </summary>
    internal static long Len(object? x) => x switch
    {
        string s => Len(s),
        byte[] bytes => bytes.Length,
        PyTuple t => t.Count,
        IPySet set => set.Count,
        PyDict pd => pd.Count,
        IDictionary or IList => ((ICollection)x).Count,
        null or bool or long or BigInteger or double or DateOnly or PyDateTime or Enum =>
            throw new InvalidCastException($"object of type '{TypeName(x)}' has no len()"),
        _ => throw Outside(x, nameof(Len)),
    };

    /// <summary><c>d.get(key)</c>: the value, or <c>None</c> for a missing key.</summary>
    internal static TValue? Get<TKey, TValue>(IDictionary<TKey, TValue> d, TKey key)
        where TKey : notnull
        where TValue : class? =>
        d.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// <c>d.get(key)</c> on a dict whose values are a value type, for example <c>dict[str, int]</c>: the value,
    /// or null for <c>None</c>. <see cref="Get{TKey, TValue}(IDictionary{TKey, TValue}, TKey)"/> covers
    /// reference values. A constraint is not part of an overload signature, so this one has its own name.
    /// </summary>
    internal static TValue? GetNullable<TKey, TValue>(IDictionary<TKey, TValue> d, TKey key)
        where TKey : notnull
        where TValue : struct =>
        d.TryGetValue(key, out var value) ? value : null;

    /// <summary><c>d.get(key, default)</c>.</summary>
    internal static TValue Get<TKey, TValue>(IDictionary<TKey, TValue> d, TKey key, TValue @default)
        where TKey : notnull =>
        d.TryGetValue(key, out var value) ? value : @default;

    /// <summary><c>d.get(key, default)</c> on a dict with keys of any type. An unhashable key throws <see cref="InvalidCastException"/>.</summary>
    internal static object? Get(PyDict d, object? key, object? @default = null) =>
        d.TryGetValue(key, out var value) ? value : @default;

    /// <summary><c>d.setdefault(key, default)</c>: the value of the key, after the default is stored for a missing key.</summary>
    internal static TValue SetDefault<TKey, TValue>(IDictionary<TKey, TValue> d, TKey key, TValue @default)
        where TKey : notnull
    {
        if (d.TryGetValue(key, out var value))
        {
            return value;
        }

        d[key] = @default;
        return @default;
    }

    /// <summary><c>d.setdefault(key, default)</c> on a dict with keys of any type.</summary>
    internal static object? SetDefault(PyDict d, object? key, object? @default = null)
    {
        if (d.TryGetValue(key, out var value))
        {
            return value;
        }

        d[key] = @default;
        return @default;
    }

    /// <summary>
    /// <c>a, b = xs</c>. Any count other than 2 throws <see cref="ArgumentException"/> (<c>ValueError</c>)
    /// with Python's message (RULES.md R21, U14).
    /// </summary>
    internal static (T First, T Second) Unpack2<T>(IReadOnlyList<T> xs) => xs.Count switch
    {
        2 => (xs[0], xs[1]),
        < 2 => throw new ArgumentException($"not enough values to unpack (expected 2, got {xs.Count})"),
        _ => throw new ArgumentException("too many values to unpack (expected 2)"),
    };

    /// <summary>
    /// <c>copy.deepcopy(x)</c>. Lists, dicts, and sets are new objects. A value that is used twice is
    /// copied one time, and a list that holds itself is copied with the same shape. Text, numbers,
    /// bytes, dates, and enum members are not copied, as Python does not copy them. A tuple is copied
    /// only if an item changed, as Python does.
    /// </summary>
    internal static object? DeepCopy(object? x) =>
        DeepCopy(x, new Dictionary<object, object?>(ReferenceEqualityComparer.Instance));

    /// <summary>
    /// A method call on a value that can be <c>None</c>: returns the value, or throws what Python raises for
    /// <c>None.attribute</c>, an <c>AttributeError</c> (RULES.md R4).
    /// </summary>
    /// <param name="value">The value from open data.</param>
    /// <param name="attribute">The Python name of the method or the attribute that the code reads.</param>
    internal static T NotNone<T>(T? value, string attribute)
        where T : class =>
        value ?? throw new MissingMemberException($"'NoneType' object has no attribute '{attribute}'");

    /// <summary><c>value.value if hasattr(value, 'value') else value</c> for text: the text.</summary>
    internal static string EnumValue(string value) => value;

    /// <summary><c>member.value</c>: the wire value of a registered enum member (<see cref="PyEnum"/>).</summary>
    internal static object EnumValue<TEnum>(TEnum member)
        where TEnum : struct, Enum => PyEnum.Value(member);

    /// <summary>A Python <c>bool</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static bool AsBool(object? x) => x is bool b ? b : throw WrongType("bool", "bool", x);

    /// <summary>
    /// A Python <c>int</c> read from open data. A <c>bool</c> is an int in Python and gives 0 or 1. An int
    /// that a long cannot hold throws <see cref="OverflowException"/> (the limit of RULES.md R4).
    /// Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).
    /// </summary>
    internal static long AsLong(object? x) => x switch
    {
        long l => l,
        bool b => b ? 1 : 0,
        BigInteger => throw new OverflowException("Python int too large to convert to C long"),
        _ => throw WrongType("int", "long", x),
    };

    /// <summary>
    /// A Python <c>float</c> read from open data. An <c>int</c> or a <c>bool</c> is accepted, as Python
    /// accepts it where a float is expected, and converts as <c>float(x)</c> does: rounded to the nearest
    /// double, and <see cref="OverflowException"/> (<c>OverflowError</c>) above the float range.
    /// Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).
    /// </summary>
    internal static double AsDouble(object? x) => x switch
    {
        double d => d,
        long l => l,
        bool b => b ? 1 : 0,
        BigInteger big => IntToFloat(big),
        _ => throw WrongType("float", "double", x),
    };

    /// <summary>A Python <c>str</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static string AsString(object? x) => x is string s ? s : throw WrongType("str", "string", x);

    /// <summary>A Python <c>list</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static List<object?> AsList(object? x) => x is List<object?> list ? list : throw WrongType("list", "List<object?>", x);

    /// <summary>
    /// <c>for x in value</c> and <c>sep.join(value)</c> on a value from open data (RULES.md R6): the items of a list,
    /// a tuple, or a set (in insertion order), the keys of a dict, the code points of a <c>str</c>, and the ints of <c>bytes</c>. Another
    /// value throws <see cref="InvalidCastException"/> (<c>TypeError: 'X' object is not iterable</c>) at the call.
    /// </summary>
    internal static IEnumerable<object?> Iter(object? x) => x switch
    {
        string s => PyText.CodePoints(s),
        byte[] bytes => bytes.Select(b => (object?)(long)b),
        PyDict dict => dict.Keys,
        System.Collections.IDictionary dict => dict.Keys.Cast<object?>(),
        System.Collections.IEnumerable items => items.Cast<object?>(),
        _ => throw new InvalidCastException($"'{TypeName(x)}' object is not iterable"),
    };

    /// <summary>A Python <c>tuple</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static PyTuple AsTuple(object? x) => x is PyTuple t ? t : throw WrongType("tuple", "PyTuple", x);

    /// <summary>A Python <c>dict</c> with string keys read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static OrderedDictionary<string, object?> AsDict(object? x) =>
        x is OrderedDictionary<string, object?> d ? d : throw WrongType("dict", "OrderedDictionary<string, object?>", x);

    /// <summary>A Python <c>dict</c> with keys of any type read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static PyDict AsPyDict(object? x) => x is PyDict d ? d : throw WrongType("dict", "PyDict", x);

    /// <summary>A Python <c>date</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static DateOnly AsDate(object? x) => x is DateOnly d ? d : throw WrongType("datetime.date", "DateOnly", x);

    /// <summary>
    /// A Python <c>datetime</c>, aware or naive, read from open data. Another type throws
    /// <see cref="InvalidCastException"/> (<c>TypeError</c>). <see cref="AsDate"/> does not take a datetime,
    /// although a Python datetime is a date: a <see cref="DateOnly"/> would drop the time.
    /// </summary>
    internal static PyDateTime AsDateTime(object? x) => x is PyDateTime dt ? dt : throw WrongType("datetime.datetime", "PyDateTime", x);

    /// <summary>A Python <c>bytes</c> read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static byte[] AsBytes(object? x) => x is byte[] b ? b : throw WrongType("bytes", "byte[]", x);

    /// <summary>A member of an enum read from open data. Another type throws <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    internal static TEnum AsEnum<TEnum>(object? x)
        where TEnum : struct, Enum => x is TEnum e ? e : throw WrongType(typeof(TEnum).Name, typeof(TEnum).Name, x);

    /// <summary>
    /// The C name of the Python type of a value (<c>tp_name</c>), as Python's error messages show it:
    /// <c>NoneType</c>, <c>int</c>, <c>datetime.date</c>, or the class name of an enum.
    /// </summary>
    internal static string TypeName(object? x) => x switch
    {
        null => "NoneType",
        bool => "bool",
        long or BigInteger => "int",
        double => "float",
        string => "str",
        byte[] => "bytes",
        PyTuple => "tuple",
        IPySet set => set.Frozen ? "frozenset" : "set",
        PyDict or IDictionary => "dict",
        IList => "list",
        DateOnly => "datetime.date",
        PyDateTime => "datetime.datetime",
        Enum e => PyEnum.ClassName(e),
        _ => x.GetType().Name,
    };

    /// <summary>The entries of either kind of dict in order.</summary>
    internal static IEnumerable<(object? Key, object? Value)> DictEntries(object dict)
    {
        if (dict is PyDict pd)
        {
            foreach (var (key, value) in pd)
            {
                yield return (key, value);
            }

            yield break;
        }

        foreach (DictionaryEntry entry in (IDictionary)dict)
        {
            yield return (entry.Key, entry.Value);
        }
    }

    /// <summary><c>float(n)</c> for an int: the nearest double, or <see cref="OverflowException"/> above the float range.</summary>
    internal static double IntToFloat(BigInteger n)
    {
        // .NET parses decimal text to the nearest double, which is Python's rounding.
        var d = double.Parse(n.ToString(CultureInfo.InvariantCulture), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return double.IsInfinity(d) ? throw new OverflowException("int too large to convert to float") : d;
    }

    /// <summary><c>str(n)</c> for an int, with Python 3.12's limit of 4300 digits.</summary>
    internal static string IntText(BigInteger n)
    {
        var text = n.ToString(CultureInfo.InvariantCulture);
        if (text.Length - (n.Sign < 0 ? 1 : 0) > MaxIntStrDigits)
        {
            throw new ArgumentException(
                $"Exceeds the limit ({MaxIntStrDigits} digits) for integer string conversion; use sys.set_int_max_str_digits() to increase the limit");
        }

        return text;
    }

    /// <summary>
    /// <c>repr(x)</c> for a float: the shortest text that reads back as the same double, in fixed notation
    /// for an exponent from -5 to 15 and in scientific notation outside it (<c>1e+16</c>, <c>1e-05</c>).
    /// </summary>
    internal static string FloatRepr(double d)
    {
        if (double.IsNaN(d))
        {
            return "nan";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? "inf" : "-inf";
        }

        var sign = double.IsNegative(d) ? "-" : string.Empty;
        if (d == 0)
        {
            return sign + "0.0";
        }

        var (digits, point) = ShortestDigits(Math.Abs(d));
        if (point <= -4 || point > 16)
        {
            var exponent = point - 1;
            var mantissa = digits.Length > 1 ? digits[..1] + "." + digits[1..] : digits;
            return sign + mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        }

        if (point <= 0)
        {
            return sign + "0." + new string('0', -point) + digits;
        }

        if (point >= digits.Length)
        {
            return sign + digits + new string('0', point - digits.Length) + ".0";
        }

        return sign + digits[..point] + "." + digits[point..];
    }

    /// <summary>
    /// The shortest digits that read back as <paramref name="positive"/>, with no leading or trailing zero,
    /// and the position of the decimal point: the value is <c>0.DIGITS * 10^point</c>. Of the shortest
    /// texts, the one nearest to the exact value wins, as in David Gay's algorithm that Python uses.
    /// </summary>
    private static (string Digits, int Point) ShortestDigits(double positive)
    {
        // .NET's "R" text is not always the shortest next to a power of two (2^-25 gives 16 digits that
        // read back as another double). So try each length: the correctly rounded digits of that length,
        // and the decimal neighbors on each side, because the round-trip interval is not symmetric there.
        for (var length = 1; length <= 17; length++)
        {
            var text = positive.ToString("E" + (length - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var e = text.IndexOf('E', StringComparison.Ordinal);
            var mantissa = BigInteger.Parse(text[..e].Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
            var exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) - (length - 1);
            foreach (var candidate in new[] { mantissa, mantissa - 1, mantissa + 1 })
            {
                var candidateText = candidate.ToString(CultureInfo.InvariantCulture);
                if (candidate.Sign > 0
                    && double.Parse(candidateText + "E" + exponent.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture) == positive)
                {
                    var digits = candidateText.TrimEnd('0');
                    return (digits, candidateText.Length + exponent);
                }
            }
        }

        throw new InvalidOperationException("no round-trip text within 17 digits");
    }

    private static string DateIso(DateOnly d) =>
        string.Create(CultureInfo.InvariantCulture, $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}");

    private static void WriteRepr(StringBuilder sb, object? x, HashSet<object> active)
    {
        switch (x)
        {
            case null:
                sb.Append("None");
                return;
            case bool b:
                sb.Append(b ? "True" : "False");
                return;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;
            case BigInteger big:
                sb.Append(IntText(big));
                return;
            case double d:
                sb.Append(FloatRepr(d));
                return;
            case string s:
                sb.Append(ReprStr(s));
                return;
            case byte[] bytes:
                sb.Append(ReprBytes(bytes));
                return;
            case DateOnly date:
                sb.Append(CultureInfo.InvariantCulture, $"datetime.date({date.Year}, {date.Month}, {date.Day})");
                return;
            case PyDateTime dateTime:
                sb.Append(dateTime.Repr());
                return;
            case Enum e:
                sb.Append('<').Append(PyEnum.ClassName(e)).Append('.').Append(PyEnum.Name(e)).Append(": ");
                WriteRepr(sb, PyEnum.Value(e), active);
                sb.Append('>');
                return;
            case PyTuple or IPySet or PyDict or IDictionary or IList:
                break;
            default:
                throw Outside(x, nameof(Repr));
        }

        if (!active.Add(x))
        {
            // Py_ReprEnter: a container that is already being printed.
            sb.Append(x switch
            {
                PyTuple => "(...)",
                IPySet set => TypeName(set) + "(...)",
                PyDict or IDictionary => "{...}",
                _ => "[...]",
            });
            return;
        }

        try
        {
            switch (x)
            {
                case PyTuple t:
                    sb.Append('(');
                    WriteItems(sb, t, active);
                    sb.Append(t.Count == 1 ? ",)" : ")");
                    break;
                case IPySet set when set.Count == 0:
                    sb.Append(set.Frozen ? "frozenset()" : "set()");
                    break;
                case IPySet set:
                    sb.Append(set.Frozen ? "frozenset({" : "{");
                    WriteItems(sb, set.Items, active);
                    sb.Append(set.Frozen ? "})" : "}");
                    break;
                case PyDict or IDictionary:
                    sb.Append('{');
                    var first = true;
                    foreach (var (key, value) in DictEntries(x))
                    {
                        sb.Append(first ? string.Empty : ", ");
                        first = false;
                        WriteRepr(sb, key, active);
                        sb.Append(": ");
                        WriteRepr(sb, value, active);
                    }

                    sb.Append('}');
                    break;
                default:
                    sb.Append('[');
                    WriteItems(sb, ((IList)x).Cast<object?>(), active);
                    sb.Append(']');
                    break;
            }
        }
        finally
        {
            active.Remove(x);
        }
    }

    private static void WriteItems(StringBuilder sb, IEnumerable<object?> items, HashSet<object> active)
    {
        var first = true;
        foreach (var item in items)
        {
            sb.Append(first ? string.Empty : ", ");
            first = false;
            WriteRepr(sb, item, active);
        }
    }

    /// <summary><c>repr(b)</c> for bytes: the quote rule of <c>repr(str)</c>, and <c>\xhh</c> for each byte outside printable ASCII.</summary>
    private static string ReprBytes(byte[] bytes)
    {
        var quote = bytes.Contains((byte)'\'') && !bytes.Contains((byte)'"') ? '"' : '\'';
        var sb = new StringBuilder("b").Append(quote);
        foreach (var b in bytes)
        {
            if (b == quote || b == '\\')
            {
                sb.Append('\\').Append((char)b);
            }
            else if (b == '\t')
            {
                sb.Append("\\t");
            }
            else if (b == '\n')
            {
                sb.Append("\\n");
            }
            else if (b == '\r')
            {
                sb.Append("\\r");
            }
            else if (b is < 0x20 or >= 0x7F)
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\x{b:x2}");
            }
            else
            {
                sb.Append((char)b);
            }
        }

        return sb.Append(quote).ToString();
    }

    private static object? DeepCopy(object? x, Dictionary<object, object?> memo)
    {
        if (x is null or bool or long or BigInteger or double or string or byte[] or Enum)
        {
            return x;
        }

        if (x is DateOnly date)
        {
            // Python copies a date through its reduce protocol and gets a new object. A tuple that holds
            // a date is therefore a new tuple. A new box of the value gives the same identity result.
            return date;
        }

        if (x is PyDateTime dateTime)
        {
            // The same holds for a datetime: the value, in a new box.
            return dateTime;
        }

        if (memo.TryGetValue(x, out var done))
        {
            return done;
        }

        switch (x)
        {
            case List<object?> list:
                var listCopy = new List<object?>(list.Count);
                memo[x] = listCopy;
                foreach (var item in list)
                {
                    listCopy.Add(DeepCopy(item, memo));
                }

                return listCopy;
            case OrderedDictionary<string, object?> dict:
                var dictCopy = new OrderedDictionary<string, object?>(dict.Count);
                memo[x] = dictCopy;
                foreach (var (key, value) in dict)
                {
                    dictCopy[key] = DeepCopy(value, memo);
                }

                return dictCopy;
            case PyDict pd:
                var pdCopy = new PyDict();
                memo[x] = pdCopy;
                foreach (var (key, value) in pd)
                {
                    pdCopy[DeepCopy(key, memo)] = DeepCopy(value, memo);
                }

                return pdCopy;
            case PyTuple t:
                // copy._deepcopy_tuple: copy the items, then keep the tuple if no item changed.
                var items = t.Select(item => DeepCopy(item, memo)).ToArray();
                if (memo.TryGetValue(x, out var again))
                {
                    return again;
                }

                object tupleCopy = items.Select((item, i) => ReferenceEquals(item, t[i])).All(same => same) ? t : new PyTuple(items);
                memo[x] = tupleCopy;
                return tupleCopy;
            case IPySet set:
                var setCopy = set.DeepCopy(item => DeepCopy(item, memo));
                memo[x] = setCopy;
                return setCopy;
            default:
                throw Outside(x, nameof(DeepCopy));
        }
    }

    /// <summary>
    /// The error of a typed read. It names the Python type and the C# type on each side, because typed data
    /// such as a <c>List&lt;string&gt;</c> is a Python list too, but it is not the open data that the read takes.
    /// </summary>
    private static InvalidCastException WrongType(string expected, string expectedClr, object? x) =>
        new($"expected {expected} as {expectedClr}, got {TypeName(x)} as {ClrName(x?.GetType())}");

    private static string ClrName(Type? type)
    {
        if (type is null)
        {
            return "null";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(ClrName)) + ">";
    }

    private static InvalidOperationException Outside(object x, string helper) =>
        new($"Py.{helper}: the type {x.GetType().FullName} is not in the Python value model (RULES.md R6)");
}
