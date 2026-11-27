namespace Graphiti.Internal;

using System.Net;
using System.Numerics;
using System.Text.Json;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;

/// <summary>
/// The chat adapter of the public surface design: the ported <see cref="LLMClient"/> on a host
/// <see cref="IChatClient"/> (decisions D17, D28, D29, D31). The base class does the shared work: the attribute
/// preamble, the schema text, the language instruction, input cleaning, the cache, and the retry.
/// </summary>
internal sealed class ChatClientLlmClient : LLMClient
{
    // The prompt name of the current generate_response call. _generate_response does not receive it.
    private static readonly AsyncLocal<string?> CurrentPromptName = new();

    private readonly IChatClient chatClient;
    private readonly double? temperature;
    private readonly Func<Exception, ModelCallFailure>? classifyFailure;
    private readonly string providerName;

    /// <summary>Initializes a new instance of the <see cref="ChatClientLlmClient"/> class.</summary>
    /// <param name="chatClient">The host chat client.</param>
    /// <param name="config">The model ids and the token limit. Null is the default <see cref="LLMConfig"/>.</param>
    /// <param name="temperature">The option <c>Llm.Temperature</c>. Null sends no temperature.</param>
    /// <param name="classifyFailure">The option <c>Llm.ClassifyFailure</c>, or null for the default classifier only.</param>
    internal ChatClientLlmClient(
        IChatClient chatClient,
        LLMConfig? config = null,
        double? temperature = null,
        Func<Exception, ModelCallFailure>? classifyFailure = null)
        : base(config)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        this.chatClient = chatClient;

        // DESIGN: the request table sends LLMConfig.temperature "only if Llm.Temperature has a value", but the ported
        // LLMConfig.Temperature is a double that always has a value (default 1). The adapter takes the option as its
        // own nullable parameter, and LLMConfig.Temperature stays the base class value that no request reads.
        this.temperature = temperature;
        this.classifyFailure = classifyFailure;
        providerName = chatClient.GetService<ChatClientMetadata>()?.ProviderName ?? "unknown";
    }

    /// <inheritdoc/>
    internal override string PythonName => nameof(ChatClientLlmClient);

    /// <summary>One <see cref="ChatMessage"/> for each message, in order, with the role of the same name.</summary>
    /// <param name="messages">The messages of the request.</param>
    /// <returns>The chat messages.</returns>
    internal static List<ChatMessage> ToChatMessages(List<Message> messages) =>
        messages.Select(m => new ChatMessage(ToChatRole(m.Role), m.Content)).ToList();

    /// <summary>Sets the prompt name for the ambient call value, then runs the base method.</summary>
    internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        // An async method restores the value of the caller when it returns.
        CurrentPromptName.Value = promptName;
        return await base.GenerateResponseAsync(
            messages,
            responseModel,
            maxTokens,
            modelSize,
            groupId,
            promptName,
            attributeExtraction,
            model,
            cancellationToken);
    }

    /// <summary>The provider name of <see cref="ChatClientMetadata"/>, or <c>unknown</c>.</summary>
    internal override string GetProviderType() => providerName;

    /// <summary>One call of the chat client, by the request table and the reply table of the design.</summary>
    internal override async Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var chatMessages = ToChatMessages(messages);
        var options = new ChatOptions
        {
            MaxOutputTokens = checked((int)maxTokens),
            ModelId = ModelFor(modelSize, model),
            ResponseFormat = ChatResponseFormat.Json,
        };
        if (temperature is { } value)
        {
            options.Temperature = (float)value;
        }

        var promptName = CurrentPromptName.Value;
        var info = new ModelCallInfo(
            "chat",
            promptName,
            responseModel?.PythonName,
            (string)Py.EnumValue(modelSize),
            model,
            responseModel is null ? null : PyJson.Dumps(responseModel.ModelJsonSchema.ToDict()));

        ChatResponse response;
        try
        {
            response = await GraphitiModelCall.CallHostAsync(
                info, token => chatClient.GetResponseAsync(chatMessages, options, token), cancellationToken);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            // The classifier runs in line on the scheduler. An exception that it throws reaches the caller as it is.
            var failure = Classify(e);
            if (failure == ModelCallFailure.RateLimit)
            {
                throw new RateLimitException("Rate limit exceeded. Please try again later.", e);
            }

            if (failure == ModelCallFailure.ServerError)
            {
                // The ported retry knows a server error only as an HttpRequestException with a status (rule R4).
                throw new HttpRequestException(
                    "The model provider failed with a server error.", e, HttpStatusCode.InternalServerError);
            }

            throw;
        }

        // DESIGN: the reply table does not order the two tests, and it says that a filtered reply is not retried. A
        // filtered reply often has no text, so the filter test comes first. Upstream's Gemini client also tests the
        // finish reason first. Upstream's OpenAI clients test the content first, so a filtered reply with JSON text
        // gives RefusalException here and a result there.
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new RefusalException("The model provider filtered the reply.");
        }

        var text = response.Text;
        if (string.IsNullOrEmpty(text))
        {
            throw new EmptyResponseException("LLM returned an empty response");
        }

        var parsed = PyJson.Loads(text);

        // DESIGN: the reply table records usage "for each reply that parses" and also rejects JSON that is not an object.
        // Upstream records a reply that json.loads accepts, also a list, so the record comes before the object test.
        var usage = response.Usage;
        TokenTracker.Record(promptName, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);

        if (parsed is not OrderedDictionary<string, object?> result)
        {
            throw new JsonException($"The model reply is a JSON {KindOf(parsed)}, not a JSON object.");
        }

        return result;
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        "system" => ChatRole.System,
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        _ => new ChatRole(role),
    };

    private static string KindOf(object? value) => value switch
    {
        null => "null",
        string => "string",
        bool => "boolean",
        long or double or BigInteger => "number",
        List<object?> => "array",
        _ => value.GetType().Name,
    };

    private string? ModelFor(ModelSize modelSize, string? model)
    {
        if (!string.IsNullOrEmpty(model))
        {
            return model;
        }

        var sized = modelSize == ModelSize.Small && !string.IsNullOrEmpty(SmallModel) ? SmallModel : Model;
        return string.IsNullOrEmpty(sized) ? null : sized;
    }

    // DESIGN: "Llm.ClassifyFailure, then the default classifier" is read as: the host classifier first, and the default
    // classifier when the host classifier is absent or says None. The host classifier is the constructor argument. With
    // no argument, or if it says None, PortServices.ClassifyModelFailure decides, as it does for the reranker: the
    // classifier of PortServices first, and then the default classifier.
    private ModelCallFailure Classify(Exception e)
    {
        var failure = classifyFailure?.Invoke(e) ?? ModelCallFailure.None;
        return failure == ModelCallFailure.None ? PortServices.ClassifyModelFailure(e) : failure;
    }
}
