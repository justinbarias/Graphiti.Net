namespace Quickstart;

using System.Collections.Concurrent;
using System.Text.Json;
using Graphiti;
using Microsoft.Extensions.AI;

/// <summary>
/// A chat client that sends the response schema of a Graphiti model call as native structured output. Graphiti asks
/// each call for JSON mode and puts the schema in the prompt text, and a model can copy the wrong shape from that
/// text. The public design gives the host the schema through <see cref="GraphitiModelCall.Current"/> for this use
/// (decision D17). The class uses only public members. It adds no retry and changes no reply. It does not ask for
/// strict mode: OpenAI rejects the schemas of Graphiti in strict mode (they have $defs and optional properties).
/// </summary>
internal sealed class StructuredOutputChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private static long schemaCalls;
    private static long plainCalls;
    private static readonly ConcurrentDictionary<string, string> Rejected = new(StringComparer.Ordinal);

    /// <summary>Gets the count of calls where the client set a schema.</summary>
    public static long SchemaCalls => Interlocked.Read(ref schemaCalls);

    /// <summary>Gets the count of calls that passed on with no schema.</summary>
    public static long PlainCalls => Interlocked.Read(ref plainCalls);

    /// <summary>Gets each schema that the provider rejected, with the first error text.</summary>
    public static IReadOnlyDictionary<string, string> RejectedSchemas => Rejected;

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var info = GraphitiModelCall.Current;
        if (info?.ResponseSchema is not { } schemaText)
        {
            Interlocked.Increment(ref plainCalls);
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }

        var copy = options?.Clone() ?? new ChatOptions();
        using var document = JsonDocument.Parse(schemaText);
        var name = info.ResponseModel ?? "response";
        copy.ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), name);
        Interlocked.Increment(ref schemaCalls);
        try
        {
            return await base.GetResponseAsync(messages, copy, cancellationToken);
        }
        catch (Exception error) when (error is System.ClientModel.ClientResultException)
        {
            Rejected.TryAdd(name, error.Message);
            throw;
        }
    }
}
