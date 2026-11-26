namespace Graphiti.PyCompat;

/// <summary>
/// A module-level value that an upstream test replaces with <c>monkeypatch.setattr(module, 'NAME', value)</c>
/// (RULES.md R17, R25). Ported code reads it as the value itself, through the implicit conversion or
/// <see cref="Value"/>. A test replaces it with <see cref="Use"/> for its own flow of execution, so tests that run at
/// the same time do not see each other's value, and the value of the module comes back when the scope ends.
/// </summary>
/// <example>
/// <code>
/// internal static readonly PyPatch&lt;double&gt; ChunkDensityThreshold = new(PyEnv.Float("CHUNK_DENSITY_THRESHOLD", 0.15));
///
/// using (Helpers.ChunkDensityThreshold.Use(0.01))
/// {
///     Assert.True(ContentChunking.ShouldChunk(content, EpisodeType.Json));
/// }
/// </code>
/// </example>
/// <typeparam name="T">The type of the value.</typeparam>
internal sealed class PyPatch<T>
{
    // An async-local value is private to a test and to the tasks that it starts.
    private readonly AsyncLocal<Replacement?> current = new();
    private readonly T value;

    /// <summary>Initializes a new instance of the <see cref="PyPatch{T}"/> class.</summary>
    /// <param name="value">The value of the module, as Python computes it at import.</param>
    internal PyPatch(T value) => this.value = value;

    /// <summary>Gets the value: the one of the innermost <see cref="Use"/> scope of this flow, else the value of the module.</summary>
    internal T Value => current.Value is { } replacement ? replacement.Value : value;

    /// <summary>Reads the value where the code names the module constant.</summary>
    public static implicit operator T(PyPatch<T> patch) => patch.Value;

    /// <summary>
    /// <c>monkeypatch.setattr</c>: replaces the value for the current flow of execution until the result is disposed.
    /// Only a test calls it.
    /// </summary>
    internal IDisposable Use(T replacement)
    {
        var previous = current.Value;
        current.Value = new Replacement(replacement);
        return new Restore(current, previous);
    }

    /// <summary>The text of the value, as an f-string or a log call shows the module constant.</summary>
    public override string ToString() => Py.Str(Value);

    private sealed record Replacement(T Value);

    private sealed class Restore(AsyncLocal<Replacement?> current, Replacement? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
