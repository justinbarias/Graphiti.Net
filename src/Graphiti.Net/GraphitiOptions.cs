namespace Graphiti;

using Graphiti.Internal;

/// <summary>
/// The options of Graphiti, which <c>AddGraphiti</c> configures. Graphiti reads them through
/// <c>IOptions&lt;GraphitiOptions&gt;</c> and validates them at the first resolution of <c>IGraphiti</c>, and at
/// startup under the generic host. The options hold no API key for a model, because the host builds the model clients.
/// Graphiti reads no <c>.env</c> file.
/// </summary>
public sealed class GraphitiOptions
{
    /// <summary>Gets or sets a value indicating whether to store the raw content of each episode. The default is true.</summary>
    public bool StoreRawEpisodeContent { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of concurrent operations at each gather of the ingestion and search paths, or
    /// null. Null uses the environment variable <c>SEMAPHORE_LIMIT</c>, which defaults to 20. It is a limit for each
    /// gather, not a global limit.
    /// </summary>
    public int? MaxCoroutines { get; set; }

    /// <summary>Gets or sets the prefix of each span name. The default is <c>graphiti</c>.</summary>
    public string TraceSpanPrefix { get; set; } = "graphiti";

    /// <summary>Gets the options of the chat model.</summary>
    public LlmOptions Llm { get; } = new();

    /// <summary>Gets the options of the embedding model.</summary>
    public EmbeddingOptions Embedding { get; } = new();

    /// <summary>Gets the options of the default cross-encoder.</summary>
    public RerankerOptions Reranker { get; } = new();

    /// <summary>Gets the prompt overrides.</summary>
    public PromptOptions Prompts { get; } = new();

    /// <summary>Gets or sets the options of concurrent ingestion, or null. Null gives serial ingestion.</summary>
    public ConcurrentIngestionOptions? ConcurrentIngestion { get; set; }
}

/// <summary>The options of the chat model: upstream's <c>LLMConfig</c>, the failure classifier, and the model routes.</summary>
public sealed class LlmOptions
{
    /// <summary>Gets or sets the model id of the medium size, or null. Null sets no model id.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the model id of the small size, or null. Null uses <see cref="Model"/>. A route does not use it:
    /// on the route path, the model size does not select a model.
    /// </summary>
    public string? SmallModel { get; set; }

    /// <summary>
    /// Gets or sets the temperature, or null. Null sends no temperature, and the provider uses its own default. Upstream
    /// sends 1.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the maximum number of output tokens of a model call. The default is 16384.</summary>
    public int MaxTokens { get; set; } = 16384;

    /// <summary>
    /// Gets or sets a function that says what kind of failure a provider exception is, or null (decision D31). Graphiti
    /// retries a rate limit and a server error. With null, or for <see cref="ModelCallFailure.None"/>, the default
    /// knows an <see cref="HttpRequestException"/> with the status 429 or a status from 500 to 599. The function runs
    /// in line on the Graphiti scheduler: it must not block, do I/O, or call <c>IGraphiti</c>.
    /// </summary>
    public Func<Exception, ModelCallFailure>? ClassifyFailure { get; set; }

    /// <summary>
    /// Gets the model routes. A key is one of the 25 upstream prompt names, for example
    /// <c>extract_nodes.extract_message</c>, or one of the 8 group names, for example <c>dedupe_edges</c>. A prompt
    /// name wins over its group. With one or more routes, <see cref="Model"/> must have a value.
    /// </summary>
    public IDictionary<string, ModelRoute> Routes { get; } = new Dictionary<string, ModelRoute>(StringComparer.Ordinal);
}

/// <summary>The options of the embedding model.</summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Gets or sets the size of an embedding. Graphiti cuts each vector to this size. The default is the environment
    /// variable <c>EMBEDDING_DIM</c>, which defaults to 1024. An empty search query uses a zero vector with the size of
    /// the environment variable, as upstream does, so a host that changes the size sets both values.
    /// </summary>
    public int Dimensions { get; set; } = checked((int)PublicValues.EmbeddingDimensions);

    /// <summary>Gets or sets the model id of the embedding generator, or null. Null sets no model id.</summary>
    public string? Model { get; set; }
}

/// <summary>The options of the default cross-encoder, which ranks passages with the chat client.</summary>
public sealed class RerankerOptions
{
    /// <summary>
    /// Gets or sets the model id of the reranker, or null. Null sets no model id, and the chat client uses its own
    /// default. This differs from upstream, which sends <c>gpt-4.1-mini</c>.
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>The prompt overrides of upstream's <c>create_prompt_library(overrides)</c>.</summary>
public sealed class PromptOptions
{
    /// <summary>
    /// Gets the prompt overrides. A key is one of the 25 upstream prompt names, for example
    /// <c>extract_nodes.extract_message</c>. An unknown name fails at startup. A builder runs in line on the Graphiti
    /// scheduler: it must not block, do I/O, or call <c>IGraphiti</c>.
    /// </summary>
    public IDictionary<string, PromptBuilder> Overrides { get; } = new Dictionary<string, PromptBuilder>(StringComparer.Ordinal);
}

/// <summary>
/// The options of concurrent ingestion for one graph: upstream's <c>ConcurrentIngestionConfig</c>. With these options,
/// adding episodes runs the extraction and the resolution of many units of one group at the same time, and commits the
/// units in sequence order.
/// </summary>
public sealed class ConcurrentIngestionOptions
{
    /// <summary>Gets or sets the number of units of one group that run at the same time. It must be 1 or more.</summary>
    public required int Window { get; set; }

    /// <summary>Gets or sets the lease of a unit. The default is 60 seconds. It must be more than zero.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the margin before the end of a lease in which no write starts. The default is 5 seconds. It must be
    /// zero or more, and less than a third of <see cref="Lease"/>.
    /// </summary>
    public TimeSpan WriteMargin { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the interval of the poll of the state store. The default is 0.25 seconds. It must be more than zero.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(0.25);

    /// <summary>Gets or sets the lag of the search index. The default is 300 seconds. It must be zero or more.</summary>
    public TimeSpan IndexLag { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Gets or sets the maximum number of attempts to apply a unit. The default is 5. It must be 1 or more.</summary>
    public int MaxApplyAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets a function that says whether a group uses concurrent ingestion, or null for each group. The
    /// function runs in line on the Graphiti scheduler: it must not block, do I/O, or call <c>IGraphiti</c>.
    /// </summary>
    public Func<string, bool>? EnabledForGroup { get; set; }

    /// <summary>
    /// Gets or sets the path of the SQLite file of the state store, or null. Null uses the in-memory store. Graphiti
    /// closes the store when the container disposes it.
    /// </summary>
    public string? StateStorePath { get; set; }
}
