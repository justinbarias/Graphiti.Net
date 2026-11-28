namespace Graphiti.Internal;

using Graphiti.PyCompat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CoreDriver = Graphiti.Core.Driver;
using CoreIngestion = Graphiti.Core.Ingestion;

/// <summary>
/// The parts of one Graphiti registration that the builder sets: the factories of the host services and of the driver.
/// The service collection holds one instance, so that a second <c>AddGraphiti</c> call and <c>AddNeo4j</c> find it.
/// </summary>
internal sealed class GraphitiRegistration
{
    internal const string NoDriverMessage =
        "Graphiti has no graph driver. Call AddNeo4j on the builder that AddGraphiti returns.";

    internal const string NoChatClientMessage =
        "Graphiti has no chat client. Register an IChatClient in the container, for example with AddChatClient, "
        + "or call UseChatClient or UseKeyedChatClient on the builder that AddGraphiti returns.";

    internal const string NoEmbeddingGeneratorMessage =
        "Graphiti has no embedding generator. Register an IEmbeddingGenerator<string, Embedding<float>> or an "
        + "IEmbeddingGenerator<string, Embedding<double>> in the container, for example with AddEmbeddingGenerator, "
        + "or call UseEmbeddingGenerator on the builder that AddGraphiti returns.";

    /// <summary>Gets or sets the factory of the driver, which <c>AddNeo4j</c> sets.</summary>
    internal Func<IServiceProvider, CoreDriver.GraphDriver>? Driver { get; set; }

    /// <summary>Gets or sets the factory of <c>UseChatClient</c> or <c>UseKeyedChatClient</c>.</summary>
    internal Func<IServiceProvider, IChatClient>? ChatClient { get; set; }

    /// <summary>Gets or sets the factory of <c>UseEmbeddingGenerator</c> for 32-bit vectors.</summary>
    internal Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>>? FloatEmbeddingGenerator { get; set; }

    /// <summary>Gets or sets the factory of <c>UseEmbeddingGenerator</c> for 64-bit vectors.</summary>
    internal Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<double>>>? DoubleEmbeddingGenerator { get; set; }

    /// <summary>Gets or sets the factory of <c>UseCrossEncoder</c>.</summary>
    internal Func<IServiceProvider, ICrossEncoder>? CrossEncoder { get; set; }

    /// <summary>Gets or sets the factory of <c>UseChatLogProbabilities</c>.</summary>
    internal Func<IServiceProvider, IChatLogProbabilities>? ChatLogProbabilities { get; set; }

    /// <summary>
    /// <c>AddGraphiti</c>. The first call registers the services. A later call on the same collection adds its
    /// <paramref name="configure"/> action and registers nothing twice.
    /// </summary>
    internal static IGraphitiBuilder Add(IServiceCollection services, Action<GraphitiOptions>? configure)
    {
        var registration = Find(services);
        if (registration is null)
        {
            registration = new GraphitiRegistration();
            services.AddSingleton(registration);
            services.AddOptions<GraphitiOptions>().ValidateOnStart();
            services.AddSingleton<IValidateOptions<GraphitiOptions>, GraphitiOptionsValidator>();
            services.AddSingleton(sp => GraphitiFacade.Create(sp, sp.GetRequiredService<GraphitiRegistration>()));
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return new GraphitiBuilder(services, registration);
    }

    /// <summary>The registration of a builder, for <c>AddNeo4j</c>. It throws if <c>AddGraphiti</c> did not run.</summary>
    internal static GraphitiRegistration Of(IGraphitiBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Find(builder.Services)
            ?? throw new InvalidOperationException("Call AddGraphiti on the service collection before this call.");
    }

    private static GraphitiRegistration? Find(IServiceCollection services) =>
        services.FirstOrDefault(d => d.ServiceType == typeof(GraphitiRegistration) && !d.IsKeyedService)?.ImplementationInstance
            as GraphitiRegistration;
}

/// <summary>The builder of <c>AddGraphiti</c>. Each member sets one factory of the registration. The last call wins.</summary>
internal sealed class GraphitiBuilder(IServiceCollection services, GraphitiRegistration registration) : IGraphitiBuilder
{
    public IServiceCollection Services => services;

    public IGraphitiBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registration.ChatClient = factory;
        return this;
    }

    public IGraphitiBuilder UseEmbeddingGenerator(Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registration.FloatEmbeddingGenerator = factory;
        registration.DoubleEmbeddingGenerator = null;
        return this;
    }

    public IGraphitiBuilder UseEmbeddingGenerator(Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<double>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registration.DoubleEmbeddingGenerator = factory;
        registration.FloatEmbeddingGenerator = null;
        return this;
    }

    public IGraphitiBuilder UseCrossEncoder(Func<IServiceProvider, ICrossEncoder> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registration.CrossEncoder = factory;
        return this;
    }

    public IGraphitiBuilder UseKeyedChatClient(object serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceKey);
        registration.ChatClient = sp => sp.GetRequiredKeyedService<IChatClient>(serviceKey);
        return this;
    }

    public IGraphitiBuilder UseChatLogProbabilities(Func<IServiceProvider, IChatLogProbabilities> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        registration.ChatLogProbabilities = factory;
        return this;
    }
}

/// <summary>
/// The validation of <see cref="GraphitiOptions"/>. The options pattern runs it at the first read of the options, so at
/// the first resolution of <c>IGraphiti</c>, and at startup under the generic host. The rules of the ported classes run
/// on the ported objects, so a value fails as it fails in Python.
/// </summary>
internal sealed class GraphitiOptionsValidator : IValidateOptions<GraphitiOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphitiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        foreach (var (key, builder) in options.Prompts.Overrides)
        {
            CheckOverride($"Prompts.Overrides['{key}']", key, builder, failures);
        }

        if (options.Llm.Routes.Count > 0 && (options.Llm.Model is null || PyText.Strip(options.Llm.Model).Length == 0))
        {
            failures.Add("Llm.Model must have a value when Llm.Routes holds a route. It is the default model of the routes.");
        }

        foreach (var (key, route) in options.Llm.Routes)
        {
            var label = $"Llm.Routes['{key}']";
            if (!PromptRouting.IsPromptName(key) && !PromptRouting.IsGroupName(key))
            {
                failures.Add($"{label}: '{key}' is not an upstream prompt name or group name.");
            }

            if (route is null)
            {
                failures.Add($"{label} is null.");
                continue;
            }

            if (route.ModelId is null || PyText.Strip(route.ModelId).Length == 0)
            {
                failures.Add($"{label}.ModelId must be a non-empty model id.");
            }

            if (route.MaxTokens is <= 0)
            {
                failures.Add($"{label}.MaxTokens must be positive.");
            }

            foreach (var (prompt, builder) in route.PromptOverrides)
            {
                CheckOverride($"{label}.PromptOverrides['{prompt}']", prompt, builder, failures);
            }
        }

        if (options.ConcurrentIngestion is { } ingestion)
        {
            try
            {
                _ = ToConfig(ingestion);
            }
            catch (ArgumentException e)
            {
                failures.Add("ConcurrentIngestion: " + e.Message);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// The options of concurrent ingestion as the ported <c>ConcurrentIngestionConfig</c>, which validates them. The state
    /// store is not set here.
    /// </summary>
    internal static CoreIngestion.ConcurrentIngestionConfig ToConfig(ConcurrentIngestionOptions options) =>
        new CoreIngestion.ConcurrentIngestionConfig
        {
            Window = options.Window,
            LeaseSeconds = options.Lease.TotalSeconds,
            WriteMarginSeconds = options.WriteMargin.TotalSeconds,
            PollIntervalSeconds = options.PollInterval.TotalSeconds,
            IndexLagSeconds = options.IndexLag.TotalSeconds,
            MaxApplyAttempts = options.MaxApplyAttempts,
            EnabledForGroup = options.EnabledForGroup,
        }.Validate();

    private static void CheckOverride(string label, string key, PromptBuilder? builder, List<string> failures)
    {
        if (!PromptRouting.IsPromptName(key))
        {
            failures.Add($"{label}: '{key}' is not one of the 25 upstream prompt names.");
        }

        if (builder is null)
        {
            failures.Add($"{label} is null.");
        }
    }
}
