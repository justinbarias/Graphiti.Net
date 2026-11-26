namespace Graphiti.JudgeHost.Core;

/// <summary>
/// A case passes an argument that the C# parameter type cannot hold (decision D20, RULES.md R22).
/// The host reports it as <c>{"error": "JudgeTypeRejected"}</c>. Product code never throws it.
/// </summary>
internal sealed class JudgeTypeRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JudgeTypeRejectedException"/> class.</summary>
    public JudgeTypeRejectedException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeTypeRejectedException"/> class.</summary>
    public JudgeTypeRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeTypeRejectedException"/> class.</summary>
    public JudgeTypeRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A defect of the host or of a binding, not a result of the case: a binding that reads a parameter that its
/// signature does not name, a signature that does not fit its method, or an exception with no Python name.
/// The host reports it as <c>{"host_error": ...}</c>, which never matches a golden result.
/// </summary>
internal sealed class JudgeHostErrorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JudgeHostErrorException"/> class.</summary>
    public JudgeHostErrorException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeHostErrorException"/> class.</summary>
    public JudgeHostErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeHostErrorException"/> class.</summary>
    public JudgeHostErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A case needs a value that no binding file registered: a <c>$ref</c>, <c>$enum</c>, or <c>$model</c> path of a unit
/// that is not ported yet, or a tag of the flow layer. The host reports it as <c>{"error": "JudgeNotBound"}</c>.
/// </summary>
internal sealed class JudgeNotBoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JudgeNotBoundException"/> class.</summary>
    public JudgeNotBoundException()
    {
        Path = string.Empty;
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeNotBoundException"/> class.</summary>
    public JudgeNotBoundException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeNotBoundException"/> class.</summary>
    public JudgeNotBoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        Path = string.Empty;
    }

    /// <summary>Initializes a new instance of the <see cref="JudgeNotBoundException"/> class for a tag and a path.</summary>
    public JudgeNotBoundException(string tag, string path, string message)
        : base(message)
    {
        Tag = tag;
        Path = path;
    }

    /// <summary>Gets the tag that needs the binding, for example <c>$model</c> or <c>$step</c>.</summary>
    public string? Tag { get; }

    /// <summary>Gets the <c>module:attr</c> path with no entry, or the empty text for a flow tag.</summary>
    public string Path { get; }
}
