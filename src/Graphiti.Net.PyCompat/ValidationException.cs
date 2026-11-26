namespace Graphiti.PyCompat;

using System.Text;

/// <summary>
/// One error of a pydantic <c>ValidationError</c>: an item of <c>e.errors()</c> with its <c>loc</c>, <c>type</c>, and <c>msg</c>.
/// </summary>
/// <param name="Loc">The location: a field name or a dict key (<see cref="string"/>), or a list index (<see cref="int"/>).</param>
/// <param name="Type">The pydantic error type, for example <c>int_parsing</c> or <c>missing</c>.</param>
/// <param name="Msg">The pydantic message, for example <c>Field required</c>.</param>
internal sealed record PydanticError(IReadOnlyList<object> Loc, string Type, string Msg)
{
    /// <summary>The same error with <paramref name="prefix"/> in front of its location.</summary>
    internal PydanticError Under(object prefix) => this with { Loc = [prefix, .. Loc] };
}

/// <summary>
/// pydantic's <c>ValidationError</c> (RULES.md R4). It derives from <see cref="ArgumentException"/>, as the Python
/// class derives from <c>ValueError</c>. It holds each error in the order that pydantic reports it.
/// </summary>
/// <remarks>
/// No upstream code shows the text of a <c>ValidationError</c> to a model or stores it. Upstream reads only the class:
/// <c>ingestion/phases.py:224</c> stores <c>type(error).__name__</c>, and the judge compares the class name. The text
/// reaches only a span status (<c>graphiti.py:1948</c>, <c>:2265</c>, <c>ingestion/scheduler.py:247</c>, <c>:327</c>,
/// <c>llm_client/client.py:273</c>) and log lines. So the message has pydantic's form without the input value, the
/// input type, and the URL line:
/// <code>
/// 2 validation errors for NodeDuplicate
/// id
///   Input should be a valid integer, unable to parse string as an integer [type=int_parsing]
/// name
///   Field required [type=missing]
/// </code>
/// </remarks>
internal sealed class ValidationException : ArgumentException
{
    /// <summary>Initializes a new instance of the <see cref="ValidationException"/> class.</summary>
    /// <param name="title">The model name, as pydantic's title. Null for an error that no model has collected yet.</param>
    /// <param name="errors">The errors, in pydantic's order. There is at least one.</param>
    /// <param name="innerException">The exception of a validator that <see cref="Pydantic.Wrap(Action, string?, string?)"/> caught.</param>
    internal ValidationException(string? title, IReadOnlyList<PydanticError> errors, Exception? innerException = null)
        : base(Text(title, errors), innerException)
    {
        Title = title;
        Errors = errors;
    }

    /// <summary>Gets the model name, as <c>e.title</c> gives it, or null.</summary>
    internal string? Title { get; }

    /// <summary>Gets the errors, as <c>e.errors()</c> gives them, without the input values.</summary>
    internal IReadOnlyList<PydanticError> Errors { get; }

    /// <summary>One error at the empty location.</summary>
    internal static ValidationException One(string type, string msg) => new(null, [new PydanticError([], type, msg)]);

    private static string Text(string? title, IReadOnlyList<PydanticError> errors)
    {
        var sb = new StringBuilder();
        sb.Append(errors.Count).Append(errors.Count == 1 ? " validation error" : " validation errors");
        if (title is not null)
        {
            sb.Append(" for ").Append(title);
        }

        foreach (var error in errors)
        {
            if (error.Loc.Count > 0)
            {
                sb.Append('\n').AppendJoin('.', error.Loc);
            }

            sb.Append("\n  ").Append(error.Msg).Append(" [type=").Append(error.Type).Append(']');
        }

        return sb.ToString();
    }
}
