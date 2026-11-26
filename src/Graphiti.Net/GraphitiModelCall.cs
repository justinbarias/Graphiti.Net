namespace Graphiti;

using Graphiti.PyCompat;

/// <summary>
/// The ambient value of the model call that Graphiti makes now. The adapters set it while they await a host service,
/// and it flows with the execution context to the thread pool. Outside such a call it is null.
/// </summary>
public static class GraphitiModelCall
{
    private static readonly AsyncLocal<ModelCallInfo?> Value = new();

    /// <summary>Gets the facts of the current model call, or null outside a call.</summary>
    public static ModelCallInfo? Current => Value.Value;

    /// <summary>
    /// Calls a host service on the thread pool with <paramref name="info"/> as <see cref="Current"/>, and resumes on
    /// the scheduler (decision D33). The caller's own value does not change.
    /// </summary>
    /// <typeparam name="T">The result type of the host call.</typeparam>
    /// <param name="info">The facts of the call.</param>
    /// <param name="call">The host call.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the host call.</returns>
    internal static Task<T> CallHostAsync<T>(
        ModelCallInfo info, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // This method is not async, so the value that it sets belongs to the caller's context. The thread-pool call
        // captures the context with the value, and the finally block gives the caller its own value back.
        var previous = Value.Value;
        Value.Value = info;
        try
        {
            return Asyncio.CallHostAsync(call, cancellationToken);
        }
        finally
        {
            Value.Value = previous;
        }
    }
}
