namespace Graphiti.Internal;

using System.Diagnostics;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Core = Graphiti.Core;
using CoreCrossEncoder = Graphiti.Core.CrossEncoder;
using CoreDriver = Graphiti.Core.Driver;
using CoreEmbedder = Graphiti.Core.Embedder;
using CoreIngestion = Graphiti.Core.Ingestion;
using CoreLlm = Graphiti.Core.LlmClient;
using CorePrompts = Graphiti.Core.Prompts;

/// <summary>
/// The facade of the public surface design: it builds <see cref="Core.GraphitiCore"/> from the container, maps each
/// public call to the ported call on the Graphiti scheduler, and closes the core when the container disposes it.
/// </summary>
internal sealed partial class GraphitiFacade : IAsyncDisposable, IDisposable
{
    /// <summary>The one <see cref="ActivitySource"/> of Graphiti (lead choice L9). A host turns it on with <c>AddSource("Graphiti.Net")</c>.</summary>
    internal static readonly ActivitySource ActivitySource = new("Graphiti.Net");

    private readonly Core.GraphitiCore core;
    private int closed;

    internal GraphitiFacade(Core.GraphitiCore core)
    {
        this.core = core;
    }

    /// <summary>Gets the ported instance under the facade.</summary>
    internal Core.GraphitiCore Ported => core;

    /// <summary>
    /// The first resolution of the facade. It validates the options, checks the three required parts, sets the
    /// process-wide services of ported code (decision D23), and builds the core with the four objects that it always
    /// passes (rule R1).
    /// </summary>
    internal static GraphitiFacade Create(IServiceProvider services, GraphitiRegistration registration)
    {
        var options = services.GetRequiredService<IOptions<GraphitiOptions>>().Value;

        var driverFactory = registration.Driver ?? throw new InvalidOperationException(GraphitiRegistration.NoDriverMessage);
        var chatClient = registration.ChatClient?.Invoke(services)
            ?? services.GetService<IChatClient>()
            ?? throw new InvalidOperationException(GraphitiRegistration.NoChatClientMessage);
        var embedderConfig = new CoreEmbedder.EmbedderConfig { EmbeddingDim = options.Embedding.Dimensions };
        var embedder = CreateEmbedder(services, registration, embedderConfig, options.Embedding.Model)
            ?? throw new InvalidOperationException(GraphitiRegistration.NoEmbeddingGeneratorMessage);

        PortServices.LoggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        PortServices.Time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        PortServices.ModelFailureClassifier = options.Llm.ClassifyFailure ?? PortServices.DefaultClassifyModelFailure;

        var llm = options.Llm;
        ChatClientLlmClient NewChatAdapter(IChatClient client) => new(
            client,
            new CoreLlm.LLMConfig(model: llm.Model, maxTokens: llm.MaxTokens, smallModel: llm.SmallModel),
            llm.Temperature,
            llm.ClassifyFailure);

        var mainClient = NewChatAdapter(chatClient);
        CoreCrossEncoder.CrossEncoderClient crossEncoder = registration.CrossEncoder is { } crossEncoderFactory
            ? new CrossEncoderBridge(crossEncoderFactory(services))
            : new CoreCrossEncoder.OpenAIRerankerClient(
                new CoreLlm.LLMConfig(model: options.Reranker.Model),
                new ChatClientLogProbabilityChat(chatClient, registration.ChatLogProbabilities?.Invoke(services)));

        CoreLlm.LLMClient? llmClient = mainClient;
        CorePrompts.IPromptLibrary? promptLibrary = null;
        CoreLlm.LLMRuntime? llmRuntime = null;
        if (llm.Routes.Count > 0)
        {
            // With routes, the runtime owns the prompts and the models, and Graphiti gets no client and no library.
            var mainTransport = new CoreLlm.LLMTransport(mainClient);
            var keyedTransports = new Dictionary<object, CoreLlm.LLMTransport>();
            CoreLlm.LLMModel ToModel(ModelRoute route)
            {
                var transport = mainTransport;
                if (route.ChatClientKey is { } key && !keyedTransports.TryGetValue(key, out transport))
                {
                    var keyedClient = NewChatAdapter(services.GetRequiredKeyedService<IChatClient>(key));

                    // Lead choice L13: each chat adapter of one Graphiti shares one usage tracker.
                    keyedClient.TokenTracker = mainClient.TokenTracker;
                    transport = new CoreLlm.LLMTransport(keyedClient);
                    keyedTransports[key] = transport;
                }

                return transport.Model(
                    route.ModelId,
                    promptOverrides: PromptRouting.ToPromptOverrides(route.PromptOverrides),
                    maxTokens: route.MaxTokens);
            }

            var routes = llm.Routes.ToDictionary(r => r.Key, r => ToModel(r.Value), StringComparer.Ordinal);
            llmRuntime = new CoreLlm.LLMRuntime(
                mainTransport.Model(llm.Model!),
                routes: PromptRouting.ToPromptRoutes(routes),
                promptOverrides: PromptRouting.ToPromptOverrides(options.Prompts.Overrides));
            llmClient = null;
        }
        else if (options.Prompts.Overrides.Count > 0)
        {
            promptLibrary = CorePrompts.Lib.CreatePromptLibrary(PromptRouting.ToLibraryOverrides(options.Prompts.Overrides));
        }

        CoreIngestion.ConcurrentIngestionConfig? concurrentIngestion = null;
        if (options.ConcurrentIngestion is { } ingestion)
        {
            concurrentIngestion = GraphitiOptionsValidator.ToConfig(ingestion);
        }

        var driver = driverFactory(services);
        try
        {
            if (concurrentIngestion is not null && options.ConcurrentIngestion!.StateStorePath is { } path)
            {
                concurrentIngestion.StateStore = new CoreIngestion.SQLiteIngestionStateStore(path);
            }

            return new GraphitiFacade(new Core.GraphitiCore(
                llmClient: llmClient,
                embedder: embedder,
                crossEncoder: crossEncoder,
                storeRawEpisodeContent: options.StoreRawEpisodeContent,
                graphDriver: driver,
                maxCoroutines: options.MaxCoroutines,
                tracer: ActivitySource,
                traceSpanPrefix: options.TraceSpanPrefix,
                promptLibrary: promptLibrary,
                llmRuntime: llmRuntime,
                concurrentIngestion: concurrentIngestion));
        }
        catch (Exception)
        {
            Asyncio.Run(async () =>
            {
                if (concurrentIngestion?.StateStore is { } store)
                {
                    await store.CloseAsync(CancellationToken.None);
                }

                await driver.CloseAsync(CancellationToken.None);
            }).GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>Calls <c>GraphitiCore.close</c> one time. A second call does nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            await Asyncio.Run(() => core.CloseAsync(CancellationToken.None)).ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Calls <c>GraphitiCore.close</c> one time and waits for it. A second call does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            Asyncio.Run(() => core.CloseAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    private static EmbeddingGeneratorEmbedder? CreateEmbedder(
        IServiceProvider services, GraphitiRegistration registration, CoreEmbedder.EmbedderConfig config, string? model)
    {
        if (registration.FloatEmbeddingGenerator is { } floatFactory)
        {
            return new EmbeddingGeneratorEmbedder(floatFactory(services), config, model);
        }

        if (registration.DoubleEmbeddingGenerator is { } doubleFactory)
        {
            return new EmbeddingGeneratorEmbedder(doubleFactory(services), config, model);
        }

        // The generator of 32-bit vectors first, then the one of 64-bit vectors.
        if (services.GetService<IEmbeddingGenerator<string, Embedding<float>>>() is { } floatGenerator)
        {
            return new EmbeddingGeneratorEmbedder(floatGenerator, config, model);
        }

        return services.GetService<IEmbeddingGenerator<string, Embedding<double>>>() is { } doubleGenerator
            ? new EmbeddingGeneratorEmbedder(doubleGenerator, config, model)
            : null;
    }
}
