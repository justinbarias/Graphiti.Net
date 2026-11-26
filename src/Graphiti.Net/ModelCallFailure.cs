namespace Graphiti;

/// <summary>
/// What kind of failure a call to a model was. The option <c>Llm.ClassifyFailure</c> maps a provider
/// exception to one of these values, so that Graphiti retries a rate limit and a server error as
/// upstream does (decision D31).
/// </summary>
public enum ModelCallFailure
{
    /// <summary>The exception is not a failure that Graphiti retries.</summary>
    None = 0,

    /// <summary>The provider refused the call because of a rate limit, for example HTTP status 429.</summary>
    RateLimit = 1,

    /// <summary>The provider failed on its side, for example an HTTP status from 500 to 599.</summary>
    ServerError = 2,
}
