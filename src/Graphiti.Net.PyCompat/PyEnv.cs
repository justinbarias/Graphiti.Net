namespace Graphiti.PyCompat;

/// <summary>
/// Reads the process environment with the conversions of <c>os.getenv</c> and <c>os.environ.get</c>.
/// A test gives values through <see cref="Use"/>, so it never changes the real environment.
/// </summary>
internal static class PyEnv
{
    // The one seam. An async-local value is private to a test and to the tasks that it starts,
    // so tests that run in parallel do not see each other.
    private static readonly AsyncLocal<Func<string, string?>?> Override = new();

    /// <summary>
    /// Replaces the environment reader for the current flow of execution until the result is disposed.
    /// A reader returns <c>null</c> for a name that is not set.
    /// </summary>
    internal static IDisposable Use(Func<string, string?> reader)
    {
        var previous = Override.Value;
        Override.Value = reader;
        return new Restore(previous);
    }

    /// <summary>
    /// <c>os.environ.get(name, default)</c> and <c>os.getenv(name, default)</c>.
    /// A value that is set and empty is returned as it is, and does not select the default.
    /// </summary>
    internal static string? Str(string name, string? defaultValue = null) => Read(name) ?? defaultValue;

    /// <summary>
    /// <c>int(os.getenv(name, default))</c>. The default goes through <c>int()</c> too, so it is
    /// returned as it is. A value that does not parse throws <see cref="ArgumentException"/>
    /// (<c>ValueError</c>), and a value beyond a long throws <see cref="OverflowException"/>.
    /// </summary>
    internal static long Int(string name, long defaultValue) =>
        Read(name) is { } text ? Py.Int(text) : defaultValue;

    /// <summary>
    /// <c>float(os.getenv(name, default))</c>. A value that does not parse throws
    /// <see cref="ArgumentException"/> (<c>ValueError</c>).
    /// </summary>
    internal static double Float(string name, double defaultValue) =>
        Read(name) is { } text ? Py.Float(text) : defaultValue;

    /// <summary>
    /// <c>bool(os.getenv(name, False))</c>. It is true for each value that is not empty, also for
    /// <c>"false"</c> and <c>"0"</c>. A name that is not set gives false.
    /// </summary>
    internal static bool Truthy(string name) => Read(name) is { Length: > 0 };

    private static string? Read(string name) =>
        (Override.Value ?? Environment.GetEnvironmentVariable)(name);

    private sealed class Restore(Func<string, string?>? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
