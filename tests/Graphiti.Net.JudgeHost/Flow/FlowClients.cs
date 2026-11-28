namespace Graphiti.JudgeHost.Flow;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;

/// <summary>
/// The <c>llm</c> seam: <c>JudgeLLMClient._generate_response</c> of <c>judge_clients.py</c>, under the product adapter
/// <c>ChatClientLlmClient</c>. It records each request, looks up the stored reply by the reply key, and returns the reply
/// as JSON text. A request with no stored reply is listed and throws <see cref="JudgeMissingReplyException"/>.
/// </summary>
internal sealed class FlowChatClient : IChatClient
{
    private readonly FlowRecorder recorder;
    private readonly Dictionary<string, string> replies = new(StringComparer.Ordinal);

    internal FlowChatClient(FlowRecorder recorder, string? repliesPath)
    {
        this.recorder = recorder;
        if (repliesPath is not null && File.Exists(repliesPath))
        {
            foreach (var line in File.ReadAllText(repliesPath).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var row = JsonDocument.Parse(line);
                replies[row.RootElement.GetProperty("key").GetString()!] = row.RootElement.GetProperty("reply").GetRawText();
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var info = GraphitiModelCall.Current;
        var list = messages.ToList();
        recorder.AddLlm(new JsonObject
        {
            ["messages"] = new JsonArray([.. list.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role.Value, ["content"] = m.Text })]),
            ["response_model"] = info?.ResponseModel,
            ["max_tokens"] = options?.MaxOutputTokens,
            ["model_size"] = info?.ModelSize,
            ["model"] = info?.Model,
        });

        // _key: the masked messages, and the SHA-256 of json.dumps([name, masked], ensure_ascii=False, separators=(',', ':')).
        var masked = list.Select(m => (object?)new List<object?> { m.Role.Value, recorder.Mask(m.Text) }).ToList();
        var text = PyJson.Dumps(new List<object?> { info?.ResponseModel, masked }, ensureAscii: false, compact: true);
        var key = Convert.ToHexStringLower(SHA256.HashData(PyText.EncodeUtf8(text)));
        if (replies.TryGetValue(key, out var reply))
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        recorder.AddMissing(new JsonObject
        {
            ["key"] = key,
            ["response_model"] = info?.ResponseModel,
            ["messages"] = Codec.Encode(masked),
        });
        throw new JudgeMissingReplyException($"no stored reply for request {key[..12]}");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The judge chat client has no streaming call.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// The <c>embed</c> seam: <c>JudgeEmbedder.create</c> and <c>create_batch</c>. The adapter names the call in
/// <see cref="ModelCallInfo.Operation"/>. <c>create</c> embeds the items joined with a space, as Python does.
/// </summary>
internal sealed class FlowEmbeddingGenerator(FlowRecorder recorder) : IEmbeddingGenerator<string, Embedding<double>>
{
    public Task<GeneratedEmbeddings<Embedding<double>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        if (GraphitiModelCall.Current?.Operation == "embed_batch")
        {
            recorder.AddEmbed(new JsonObject { ["batch"] = Codec.Encode(list) });
            return Task.FromResult(new GeneratedEmbeddings<Embedding<double>>(list.Select(text => new Embedding<double>(JudgeEmbedding.Embed(text)))));
        }

        recorder.AddEmbed(Codec.Encode(list)!);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<double>>([new Embedding<double>(JudgeEmbedding.Embed(string.Join(' ', list)))]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// The <c>rerank</c> seam: <c>JudgeCrossEncoder.rank</c>. A passage scores <c>(cosine + 1) / 2</c> of the judge
/// embeddings, and a tie keeps the input order.
/// </summary>
internal sealed class FlowCrossEncoder(FlowRecorder recorder) : ICrossEncoder
{
    public Task<IReadOnlyList<(string Passage, double Score)>> RankAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
    {
        recorder.AddRerank(new JsonObject { ["query"] = query, ["passages"] = Codec.Encode(passages.ToList()) });
        var queryVector = JudgeEmbedding.Embed(query);
        var scored = new List<(int Index, string Passage, double Score)>();
        for (var index = 0; index < passages.Count; index++)
        {
            var vector = JudgeEmbedding.Embed(passages[index]);
            var dot = 0.0;
            for (var i = 0; i < Math.Min(queryVector.Length, vector.Length); i++)
            {
                dot += queryVector[i] * vector[i];
            }

            scored.Add((index, passages[index], (dot + 1.0) / 2.0));
        }

        IReadOnlyList<(string, double)> ranked = [.. scored.OrderByDescending(s => s.Score).ThenBy(s => s.Index).Select(s => (s.Passage, s.Score))];
        return Task.FromResult(ranked);
    }
}

/// <summary><c>JudgeMissingReply</c>: a request with no stored reply. The step error carries this Python name.</summary>
internal sealed class JudgeMissingReplyException(string message) : Exception(message);

/// <summary><c>JudgeDatabaseNotEmpty</c>: a node outside the groups of the case exists. The host answers a <c>host_error</c>.</summary>
#pragma warning disable CA1711 // The Python name is the class name of the host error.
internal sealed class JudgeDatabaseNotEmpty() : Exception("The database holds nodes outside the groups of this case.");
#pragma warning restore CA1711
