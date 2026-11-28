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

    /// <summary>The message of options with routes and no model. The validator rejects them first.</summary>
    internal const string RoutesNeedModelMessage = "Llm.Model must have a value when Llm.Routes holds a route.";

    /// <summary>The message of a sync dispose on the thread of the Graphiti scheduler.</summary>
    internal const string DisposeOnSchedulerMessage =
        "Graphiti cannot close with the sync Dispose on the thread of the Graphiti scheduler, because the close waits " +
        "for work on that thread. Call DisposeAsync.";

    // The facades that are live, in the order of their build. PortServices is process-wide (decision D23), so each
    // container shares it. A first resolution applies the services of its facade before the build, because the build
    // reads them. It applies them again in the lock section that adds the facade to the live list, because a dispose
    // during the build applies the services of another facade. After that, PortServices holds the services of the
    // latest live facade, or the defaults with no live facade. A disposed container then leaves no disposed logger
    // factory in PortServices.
    private static readonly Lock LiveLock = new();
    private static readonly List<GraphitiFacade> Live = [];

    private readonly Core.GraphitiCore core;
    private readonly Ambient ambient;
    private int closed;

    private GraphitiFacade(Core.GraphitiCore core, Ambient ambient)
    {
        this.core = core;
        this.ambient = ambient;
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

        var ambient = new Ambient(
            services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            options.Llm.ClassifyFailure ?? PortServices.DefaultClassifyModelFailure);
        lock (LiveLock)
        {
            ambient.Apply();
        }

        try
        {
            var facade = new GraphitiFacade(Build(services, registration, options, driverFactory, chatClient, embedder), ambient);
            lock (LiveLock)
            {
                // F2: a Leave of another facade during the build applied the services of that facade or the defaults.
                Live.Add(facade);
                ambient.Apply();
            }

            return facade;
        }
        catch (Exception)
        {
            Leave(null);
            throw;
        }
    }

    /// <summary>Calls <c>GraphitiCore.close</c> one time. A second call does nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            try
            {
                await Asyncio.Run(() => core.CloseAsync(CancellationToken.None)).ConfigureAwait(false);
            }
            finally
            {
                Leave(this);
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Calls <c>GraphitiCore.close</c> one time and waits for it. A second call does nothing. A call on the thread of
    /// the Graphiti scheduler throws <see cref="InvalidOperationException"/>, because the wait would block the close.
    /// </summary>
    public void Dispose()
    {
        if (Volatile.Read(ref closed) != 0)
        {
            return;
        }

        if (Asyncio.IsOnLoop)
        {
            throw new InvalidOperationException(DisposeOnSchedulerMessage);
        }

        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            try
            {
                Asyncio.Run(() => core.CloseAsync(CancellationToken.None)).GetAwaiter().GetResult();
            }
            finally
            {
                Leave(this);
            }
        }

        GC.SuppressFinalize(this);
    }

    // Removes a facade from the live list, and gives PortServices the services of the latest live facade, or the
    // defaults.
    private static void Leave(GraphitiFacade? facade)
    {
        lock (LiveLock)
        {
            if (facade is not null)
            {
                Live.Remove(facade);
            }

            (Live.Count > 0 ? Live[^1].ambient : Ambient.Default).Apply();
        }
    }

    private static Core.GraphitiCore Build(
        IServiceProvider services,
        GraphitiRegistration registration,
        GraphitiOptions options,
        Func<IServiceProvider, CoreDriver.GraphDriver> driverFactory,
        IChatClient chatClient,
        EmbeddingGeneratorEmbedder embedder)
    {
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
                mainTransport.Model(llm.Model ?? throw new InvalidOperationException(RoutesNeedModelMessage)),
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
            if (concurrentIngestion is not null && options.ConcurrentIngestion is { StateStorePath: { } path })
            {
                concurrentIngestion.StateStore = new CoreIngestion.SQLiteIngestionStateStore(path);
            }

            return new Core.GraphitiCore(
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
                concurrentIngestion: concurrentIngestion);
        }
        catch (Exception)
        {
            CloseAfterFailedBuild(concurrentIngestion?.StateStore, driver);
            throw;
        }
    }

    // A-F6: the cleanup after a failed build. The host gets the first error. A failure of the close is logged and does
    // not replace it. The logger is the one of the failed build, read before the close starts.
    private static void CloseAfterFailedBuild(CoreIngestion.IngestionStateStore? store, CoreDriver.GraphDriver driver)
    {
        var logger = PortServices.Logger(typeof(GraphitiFacade));
        var closing = Asyncio.Run(async () =>
        {
            try
            {
                if (store is not null)
                {
                    await store.CloseAsync(CancellationToken.None);
                }
            }
            finally
            {
                await driver.CloseAsync(CancellationToken.None);
            }
        });

        // F8: on the thread of the Graphiti scheduler, a wait blocks the close that it waits for, as in the sync Dispose.
        // The close then runs after the first resolution returns, and a continuation logs its failure.
        if (Asyncio.IsOnLoop)
        {
            _ = closing.ContinueWith(
                done =>
                {
                    if (done.Exception is { } failure)
                    {
                        CloseAfterFailedBuildFailed(logger, failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0] : failure);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        try
        {
            closing.GetAwaiter().GetResult();
        }
        catch (Exception closeError)
        {
            CloseAfterFailedBuildFailed(logger, closeError);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The close after a failed build of Graphiti failed. The host gets the error of the build.")]
    private static partial void CloseAfterFailedBuildFailed(ILogger logger, Exception error);

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

    // The process-wide services of one facade (decision D23).
    private sealed record Ambient(
        ILoggerFactory LoggerFactory, TimeProvider Time, Func<Exception, ModelCallFailure> ModelFailureClassifier)
    {
        internal static readonly Ambient Default =
            new(NullLoggerFactory.Instance, TimeProvider.System, PortServices.DefaultClassifyModelFailure);

        internal void Apply()
        {
            PortServices.LoggerFactory = LoggerFactory;
            PortServices.Time = Time;
            PortServices.ModelFailureClassifier = ModelFailureClassifier;
        }
    }
}
