namespace Graphiti.PyCompat;

/// <summary>
/// A module-level value that an upstream test replaces with <c>monkeypatch.setattr(module, 'NAME', value)</c>
/// (RULES.md R17, R25). Ported code reads it with <see cref="Value"/> at each site. A test replaces it with
/// <see cref="Use"/> for its own flow of execution, so tests that run at the same time do not see each other's
/// value, and the value of the module comes back when the scope ends.
/// </summary>
/// <remarks>
/// The type has no implicit conversion to <typeparamref name="T"/>. With one, <c>X == null</c>, <c>X.Equals(v)</c>,
/// <c>var x = X</c>, and an <c>object</c> parameter read the holder and not the value, and the compiler accepts each.
/// </remarks>
/// <example>
/// <code>
/// internal static readonly PyPatch&lt;double&gt; ChunkDensityThreshold = new(PyEnv.Float("CHUNK_DENSITY_THRESHOLD", 0.15));
///
/// if (density &gt; Helpers.ChunkDensityThreshold.Value) { ... }
///
/// using (Helpers.ChunkDensityThreshold.Use(0.01))
/// {
///     Assert.True(ContentChunking.ShouldChunk(content, EpisodeType.Json));
/// }
/// </code>
/// </example>
/// <typeparam name="T">The type of the value.</typeparam>
internal sealed class PyPatch<T> : IFormattable
{
    // An async-local value is private to a test and to the tasks that it starts.
    private readonly AsyncLocal<Replacement?> current = new();
    private readonly T value;

    /// <summary>Initializes a new instance of the <see cref="PyPatch{T}"/> class.</summary>
    /// <param name="value">The value of the module, as Python computes it at import.</param>
    internal PyPatch(T value) => this.value = value;

    /// <summary>Gets the value: the one of the innermost <see cref="Use"/> scope of this flow, else the value of the module.</summary>
    internal T Value => current.Value is { } replacement ? replacement.Value : value;

    /// <summary>
    /// <c>monkeypatch.setattr</c>: replaces the value for the current flow of execution until the result is disposed.
    /// Only a test calls it, in the test method or in the constructor of the test class. A call after an
    /// <c>await</c> in an async fixture method does not reach the test. Dispose nested scopes in reverse order.
    /// </summary>
    /// <exception cref="InvalidOperationException">On dispose, if the scope is not the innermost scope of the flow.</exception>
    internal IDisposable Use(T replacement)
    {
        var mine = new Replacement(replacement);
        var scope = new Restore(current, mine, current.Value);
        current.Value = mine;
        return scope;
    }

    /// <summary>The Python text of the value, for a site that puts the holder in an interpolated string by mistake.</summary>
    public override string ToString() => Py.Str(Value);

    /// <summary>The text of the value with a format, so that a format specifier is not lost.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.IsNullOrEmpty(format) || Value is not IFormattable formattable ? ToString() : formattable.ToString(format, formatProvider);

    private sealed class Replacement(T value)
    {
        internal T Value { get; } = value;
    }

    private sealed class Restore(AsyncLocal<Replacement?> current, Replacement mine, Replacement? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (!ReferenceEquals(current.Value, mine))
            {
                throw new InvalidOperationException("Dispose the scopes of a PyPatch in reverse order, in the flow that made them.");
            }

            disposed = true;
            current.Value = previous;
        }
    }
}
