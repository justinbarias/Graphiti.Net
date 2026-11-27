// Fakes at the network boundary for the tests of the Microsoft.Extensions.AI adapters (docs/TESTING.md, principle 4):
// a scripted IChatClient, a scripted IEmbeddingGenerator, a scripted ICrossEncoder, and a scripted
// IChatLogProbabilities. Each one keeps what it received, the ambient call value, and whether it ran on the scheduler.
namespace Graphiti.Port.Evaluation;

using Graphiti;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;

/// <summary>What a scripted host service saw in one call.</summary>
internal sealed record HostCall(
    List<(string Role, string Text)> Messages, ChatOptions? Options, ModelCallInfo? Info, bool OnLoop);

/// <summary>A chat client that answers the n-th call with the n-th scripted step.</summary>
internal sealed class ScriptedChatClient(Func<int, IReadOnlyList<ChatMessage>, ChatResponse> script, string? providerName = null) : IChatClient
{
    private readonly Lock gate = new();

    internal List<HostCall> Calls { get; } = [];

    /// <summary>An action of the test that runs inside each call, for example a cancel of the caller's token.</summary>
    internal Action? DuringCall { get; set; }

    /// <summary>For each call: whether the token that the host got was canceled after <see cref="DuringCall"/>.</summary>
    internal List<bool> CanceledDuringCall { get; } = [];

    /// <summary>A reply with the given text, finish reason, and usage.</summary>
    internal static ChatResponse Reply(string? text, ChatFinishReason? finishReason = null, UsageDetails? usage = null)
    {
        var response = text is null ? new ChatResponse() : new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        response.FinishReason = finishReason;
        response.Usage = usage;
        return response;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        int index;
        lock (gate)
        {
            index = Calls.Count;
            Calls.Add(new HostCall(
                list.Select(m => (m.Role.Value, m.Text)).ToList(), options, GraphitiModelCall.Current, Asyncio.IsOnLoop));
            DuringCall?.Invoke();
            CanceledDuringCall.Add(cancellationToken.IsCancellationRequested);
        }

        return Task.FromResult(script(index, list));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The adapters never stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) && providerName is not null
            ? new ChatClientMetadata(providerName)
            : null;

    public void Dispose()
    {
    }
}

/// <summary>An embedding generator that gives each input text a scripted vector.</summary>
internal sealed class ScriptedEmbeddingGenerator<T>(Func<string, T[]> vectorOf) : IEmbeddingGenerator<string, Embedding<T>>
{
    internal List<(List<string> Inputs, EmbeddingGenerationOptions? Options, ModelCallInfo? Info, bool OnLoop)> Calls { get; } = [];

    internal Action? DuringCall { get; set; }

    internal List<bool> CanceledDuringCall { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<T>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        lock (Calls)
        {
            Calls.Add((inputs, options, GraphitiModelCall.Current, Asyncio.IsOnLoop));
            DuringCall?.Invoke();
            CanceledDuringCall.Add(cancellationToken.IsCancellationRequested);
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<T>>(inputs.Select(i => new Embedding<T>(vectorOf(i)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>A log probability hook that reads the scripted tokens of a reply from its text.</summary>
internal sealed class ScriptedLogProbabilities(Func<string, IReadOnlyList<TokenLogProbability>?> read) : IChatLogProbabilities
{
    internal List<(int TopCount, Dictionary<int, int> Bias, bool OnLoop)> Requests { get; } = [];

    internal int Reads { get; private set; }

    public void Request(ChatOptions options, int topLogProbabilityCount, IReadOnlyDictionary<int, int> logitBias)
    {
        lock (Requests)
        {
            Requests.Add((topLogProbabilityCount, logitBias.ToDictionary(), Asyncio.IsOnLoop));
        }
    }

    public IReadOnlyList<TokenLogProbability>? ReadFirstToken(ChatResponse response)
    {
        lock (Requests)
        {
            Reads++;
        }

        return read(response.Text);
    }
}

/// <summary>A host cross-encoder with a scripted result.</summary>
internal sealed class ScriptedCrossEncoder(IReadOnlyList<(string Passage, double Score)> result) : ICrossEncoder
{
    internal List<(string Query, List<string> Passages, ModelCallInfo? Info, bool OnLoop)> Calls { get; } = [];

    internal Action? DuringCall { get; set; }

    internal List<bool> CanceledDuringCall { get; } = [];

    public Task<IReadOnlyList<(string Passage, double Score)>> RankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
    {
        Calls.Add((query, passages.ToList(), GraphitiModelCall.Current, Asyncio.IsOnLoop));
        DuringCall?.Invoke();
        CanceledDuringCall.Add(cancellationToken.IsCancellationRequested);
        return Task.FromResult(result);
    }
}
