// Fakes at the network boundary of the public surface (AGENTS.md, rule 7): IChatClient, IEmbeddingGenerator, and
// ICrossEncoder. The chat client answers by the prompt name of GraphitiModelCall.Current.
namespace Graphiti.E2E.Tests;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

/// <summary>
/// A chat client with scripted replies. It reads the prompt name from <see cref="GraphitiModelCall.Current"/> and asks
/// the script for the JSON text of the reply. A prompt name that the script does not know fails the call.
/// </summary>
internal sealed class ScriptedChatClient(Func<string, string, string?> script) : IChatClient
{
    /// <summary>
    /// Gets each chat call: the prompt name, the model id of the options, the user text, the options, and the model
    /// size of <see cref="GraphitiModelCall.Current"/>.
    /// </summary>
    internal ConcurrentQueue<(string Prompt, string? ModelId, string User, ChatOptions? Options, string? ModelSize)> Calls { get; } = new();

    /// <summary>Gets each rerank call: the user text and the options.</summary>
    internal ConcurrentQueue<(string User, ChatOptions? Options)> Reranks { get; } = new();

    /// <summary>Gets or sets the reply text of a rerank call, by the user text. Null fails a rerank call.</summary>
    internal Func<string, string>? Rerank { get; set; }

    /// <summary>Gets each thread and synchronization context that a call ran on.</summary>
    internal ConcurrentQueue<(bool OnScheduler, SynchronizationContext? Context)> Places { get; } = new();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Places.Enqueue((Graphiti.PyCompat.Asyncio.IsOnLoop, SynchronizationContext.Current));
        var user = string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        if (GraphitiModelCall.Current is { Operation: "rerank" })
        {
            Reranks.Enqueue((user, options));
            var reply = Rerank?.Invoke(user) ?? throw new InvalidOperationException("The script has no rerank reply.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        var prompt = GraphitiModelCall.Current?.PromptName
            ?? throw new InvalidOperationException("The chat call has no prompt name.");
        Calls.Enqueue((prompt, options?.ModelId, user, options, GraphitiModelCall.Current?.ModelSize));
        var text = script(prompt, user)
            ?? throw new InvalidOperationException($"The script has no reply for the prompt {prompt}.");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 4 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>An embedding generator whose vector is a pure function of the text (SHA-256), with unit length.</summary>
internal sealed class HashEmbeddingGenerator(int dimensions = 1024) : IEmbeddingGenerator<string, Embedding<float>>
{
    internal int Calls;

    /// <summary>Gets or sets an action that runs at each call, after the call is counted and before the token check.</summary>
    internal Action? OnCall { get; set; }

    internal static float[] Vector(string text, int dimensions)
    {
        var values = new float[dimensions];
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        for (var block = 0; block * 8 < dimensions; block++)
        {
            var hash = SHA256.HashData([.. seed, .. BitConverter.GetBytes(block)]);
            for (var i = 0; i < 8 && block * 8 + i < dimensions; i++)
            {
                values[block * 8 + i] = BitConverter.ToInt32(hash, i * 4) / (float)int.MaxValue;
            }
        }

        var norm = MathF.Sqrt(values.Sum(v => v * v));
        return [.. values.Select(v => v / norm)];
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        // The call counts before the token check, so that a call that starts with a canceled token is seen.
        Interlocked.Increment(ref Calls);
        OnCall?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(text => new Embedding<float>(Vector(text, dimensions)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>The same generator for 64-bit vectors.</summary>
internal sealed class HashDoubleEmbeddingGenerator(int dimensions = 1024) : IEmbeddingGenerator<string, Embedding<double>>
{
    internal int Calls;

    public Task<GeneratedEmbeddings<Embedding<double>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref Calls);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<double>>(
            values.Select(text => new Embedding<double>(HashEmbeddingGenerator.Vector(text, dimensions).Select(v => (double)v).ToArray()))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>A cross-encoder that scores a passage by the share of query words that it holds.</summary>
internal sealed class WordCrossEncoder : ICrossEncoder
{
    public Task<IReadOnlyList<(string Passage, double Score)>> RankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
    {
        var words = query.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<(string, double)> ranked =
        [
            .. passages
                .Select(p => (p, (double)words.Count(w => p.ToUpperInvariant().Contains(w, StringComparison.Ordinal)) / Math.Max(1, words.Length)))
                .OrderByDescending(x => x.Item2),
        ];
        return Task.FromResult(ranked);
    }
}
