namespace Graphiti.PyCompat;

/// <summary>
/// The fields of one model as pydantic validates them: <c>ModelValidate</c> reads each field from open data, and
/// <c>Validate()</c> checks the constraints. Each error is kept in field order, and <see cref="ThrowIfAny"/> throws
/// one <see cref="ValidationException"/> with all of them, as pydantic reports each bad field in one error.
/// Unknown keys of the data are ignored, which is pydantic's default (<c>extra='ignore'</c>). No upstream model of
/// the MVP modules sets <c>extra</c>.
/// </summary>
/// <example>
/// <code>
/// internal static NodeDuplicate ModelValidate(OrderedDictionary&lt;string, object?&gt; data)
/// {
///     var f = new PydanticFields("NodeDuplicate", data);
///     var id = f.Required("id", Pydantic.Int);
///     var name = f.Required("name", Pydantic.Str);
///     var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
///     f.ThrowIfAny();
///     return new NodeDuplicate { Id = id.Value, Name = name.Value, Labels = labels.Value }.Validate();
/// }
/// </code>
/// </example>
internal sealed class PydanticFields
{
    private readonly string title;
    private readonly OrderedDictionary<string, object?>? data;
    private readonly List<PydanticError> errors = [];

    /// <summary>Initializes a new instance of the <see cref="PydanticFields"/> class.</summary>
    /// <param name="title">The Python class name of the model, which pydantic shows as the title of the error.</param>
    /// <param name="data">The open data of <c>M(**data)</c>, or null in <c>Validate()</c>, which reads no data.</param>
    internal PydanticFields(string title, OrderedDictionary<string, object?>? data = null)
    {
        this.title = title;
        this.data = data;
    }

    /// <summary>A required field, <c>x: T</c> or <c>Field(...)</c>: a missing key is the error <c>missing</c>.</summary>
    internal PydanticValue<T> Required<T>(string name, Func<object?, T> read)
    {
        if (data is null || !data.TryGetValue(name, out var value))
        {
            errors.Add(new PydanticError([name], "missing", "Field required"));
            return default;
        }

        return Read(name, value, read);
    }

    /// <summary>
    /// A field with a default, <c>= x</c>, <c>Field(default=x)</c>, or <c>Field(default_factory=f)</c>: a missing
    /// key gives <paramref name="default"/>, which is not validated, as in pydantic. A present key is read, also a
    /// <c>None</c>, which only a <c>T | None</c> reader accepts.
    /// </summary>
    internal PydanticValue<T> Default<T>(string name, Func<object?, T> read, Func<T> @default) =>
        data is not null && data.TryGetValue(name, out var value) ? Read(name, value, read) : new PydanticValue<T>(@default());

    /// <summary><c>Field(ge=limit)</c> on an <c>int</c> field.</summary>
    internal void Ge(string name, long value, long limit)
    {
        if (!(value >= limit))
        {
            Add(name, "greater_than_equal", $"Input should be greater than or equal to {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(ge=limit)</c> with an int limit on a <c>float</c> field. A <c>nan</c> fails, as in pydantic.</summary>
    internal void Ge(string name, double value, long limit)
    {
        if (!(value >= limit))
        {
            Add(name, "greater_than_equal", $"Input should be greater than or equal to {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(ge=limit)</c> with a float limit on a <c>float</c> field.</summary>
    internal void Ge(string name, double value, double limit)
    {
        if (!(value >= limit))
        {
            Add(name, "greater_than_equal", $"Input should be greater than or equal to {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(gt=limit)</c> on an <c>int</c> field.</summary>
    internal void Gt(string name, long value, long limit)
    {
        if (!(value > limit))
        {
            Add(name, "greater_than", $"Input should be greater than {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(gt=limit)</c> with an int limit on a <c>float</c> field. A <c>nan</c> fails, as in pydantic.</summary>
    internal void Gt(string name, double value, long limit)
    {
        if (!(value > limit))
        {
            Add(name, "greater_than", $"Input should be greater than {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(gt=limit)</c> with a float limit on a <c>float</c> field.</summary>
    internal void Gt(string name, double value, double limit)
    {
        if (!(value > limit))
        {
            Add(name, "greater_than", $"Input should be greater than {Py.Repr(limit)}");
        }
    }

    /// <summary><c>Field(max_length=limit)</c> on a <c>str</c> or <c>str | None</c> field: the length in code points.</summary>
    internal void MaxLength(string name, string? value, long limit)
    {
        // A str field with a constraint takes another path in pydantic-core, which rejects a lone surrogate.
        if (value is not null && value != PyText.DropLoneSurrogates(value))
        {
            Add(name, "string_unicode", "Input should be a valid string, unable to parse raw data as a unicode string");
        }
        else if (value is not null && Py.Len(value) > limit)
        {
            Add(name, "string_too_long", $"String should have at most {limit} character{(limit == 1 ? string.Empty : "s")}");
        }
    }

    /// <summary>Throws one <see cref="ValidationException"/> with each error so far, in field order, if there is one.</summary>
    internal void ThrowIfAny()
    {
        if (errors.Count > 0)
        {
            throw new ValidationException(title, [.. errors]);
        }
    }

    private PydanticValue<T> Read<T>(string name, object? value, Func<object?, T> read)
    {
        try
        {
            return new PydanticValue<T>(read(value));
        }
        catch (ValidationException e)
        {
            errors.AddRange(e.Errors.Select(error => error.Under(name)));
            return default;
        }
    }

    private void Add(string name, string type, string msg) => errors.Add(new PydanticError([name], type, msg));
}

/// <summary>
/// The result of one field read of <see cref="PydanticFields"/>. <see cref="Value"/> is valid after
/// <see cref="PydanticFields.ThrowIfAny"/> returns.
/// </summary>
/// <typeparam name="T">The C# type of the field.</typeparam>
internal readonly struct PydanticValue<T>
{
    private readonly T value;
    private readonly bool ok;

    /// <summary>Initializes a new instance of the <see cref="PydanticValue{T}"/> struct with a valid value.</summary>
    internal PydanticValue(T value)
    {
        this.value = value;
        ok = true;
    }

    /// <summary>Gets the validated value. A field with an error throws <see cref="InvalidOperationException"/>.</summary>
    internal T Value => ok ? value : throw new InvalidOperationException("PydanticValue: the field has an error. Call PydanticFields.ThrowIfAny before Value.");
}

/// <summary>
/// One entry of <c>M.ModelFields</c>, the port of <c>M.model_fields[name]</c> (RULES.md R7). Upstream reads the keys
/// and their order of a built-in model (<c>entity_types_utils.py:29</c>), and of a field of a custom type only
/// <c>is_required()</c> and the <c>max_length</c> of its metadata (<c>attribute_utils.py:84</c> to <c>:98</c>).
/// </summary>
/// <param name="IsRequired"><c>field.is_required()</c>: true for a field with no default and no default factory.</param>
/// <param name="MaxLength">The <c>max_length</c> of <c>Field(max_length=...)</c>, or null.</param>
internal sealed record PydanticField(bool IsRequired, long? MaxLength = null);
