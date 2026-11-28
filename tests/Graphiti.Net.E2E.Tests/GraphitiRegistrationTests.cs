// The registration of Graphiti with a real service container (plan 0004, layer 9, packet P9d): AddGraphiti, the
// builder, AddNeo4j, the validation of the options, and the lifetime. No test here calls Neo4j: the driver is the fake
// IDriver of Neo4jDriverFakes.cs, at the network boundary.
namespace Graphiti.E2E.Tests;

using Graphiti.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public sealed class GraphitiRegistrationTests
{
    public static TheoryData<string, string> BadOptions =>
        new()
        {
            { "route without model", "Llm.Model must have a value when Llm.Routes holds a route" },
            { "unknown override", "'extract_nodes.nope' is not one of the 25 upstream prompt names" },
            { "unknown route", "'nowhere' is not an upstream prompt name or group name" },
            { "empty route model", "Llm.Routes['dedupe_edges'].ModelId must be a non-empty model id" },
            { "route max tokens", "Llm.Routes['dedupe_edges'].MaxTokens must be positive" },
            { "window", "ConcurrentIngestion: " },
            { "write margin", "write_margin_seconds must be less than lease_seconds / 3" },
        };

    [Fact]
    public void A_missing_driver_fails_at_the_first_resolution_and_names_AddNeo4j()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(Chat());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        services.AddGraphiti();
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(Resolve(provider));
        Assert.Contains("AddNeo4j", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_chat_client_fails_at_the_first_resolution_and_names_the_calls()
    {
        var driver = new FakeClient();
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        services.AddGraphiti().AddNeo4j(_ => driver);
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(Resolve(provider));
        Assert.Contains("AddChatClient", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseChatClient", error.Message, StringComparison.Ordinal);
        Assert.Empty(driver.Calls.Names);
    }

    [Fact]
    public void A_missing_embedding_generator_fails_at_the_first_resolution_and_names_the_calls()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(Chat());
        services.AddGraphiti().AddNeo4j(_ => new FakeClient());
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(Resolve(provider));
        Assert.Contains("AddEmbeddingGenerator", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseEmbeddingGenerator", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_can_give_each_part_in_place_of_the_container()
    {
        var services = new ServiceCollection();
        services.AddGraphiti()
            .AddNeo4j(_ => new FakeClient())
            .UseChatClient(_ => Chat())
            .UseEmbeddingGenerator(_ => new HashDoubleEmbeddingGenerator())
            .UseCrossEncoder(_ => new WordCrossEncoder());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(Resolve(provider)());
    }

    [Theory]
    [MemberData(nameof(BadOptions))]
    public void Bad_options_fail_at_the_first_resolution(string kind, string message)
    {
        var services = Complete(new FakeClient(), options => Spoil(options, kind));
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(Resolve(provider));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Neo4j_options_with_no_uri_fail_at_the_first_resolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(Chat());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        services.AddGraphiti().AddNeo4j(o => o.User = "neo4j");
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(Resolve(provider));
        Assert.Contains("Neo4jOptions.Uri must have a value.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_AddGraphiti_adds_its_action_and_registers_nothing_twice()
    {
        var services = Complete(new FakeClient(), o => o.Llm.Model = "medium");
        services.AddGraphiti(o => o.Llm.SmallModel = "small");
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IGraphiti));
        var options = provider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        Assert.Equal("medium", options.Llm.Model);
        Assert.Equal("small", options.Llm.SmallModel);
    }

    [Fact]
    public void The_sync_dispose_of_the_container_closes_the_driver_one_time()
    {
        var driver = new FakeClient();
        var provider = Complete(driver).BuildServiceProvider();
        _ = Resolve(provider)();

        provider.Dispose();

        Assert.Equal(["client.close"], driver.Calls.Names);
    }

    [Fact]
    public async Task The_async_dispose_of_the_container_closes_the_driver_one_time()
    {
        var driver = new FakeClient();
        var provider = Complete(driver).BuildServiceProvider();
        _ = Resolve(provider)();

        await provider.DisposeAsync();

        Assert.Equal(["client.close"], driver.Calls.Names);
    }

    public static TheoryData<string, string> ValidatorRules =>
        new()
        {
            { "override name", "Prompts.Overrides['extract_nodes.nope']: 'extract_nodes.nope' is not one of the 25 upstream prompt names." },
            { "override null", "Prompts.Overrides['extract_nodes.extract_text'] is null." },
            { "routes need a model", "Llm.Model must have a value when Llm.Routes holds a route. It is the default model of the routes." },
            { "routes need a non-blank model", "Llm.Model must have a value when Llm.Routes holds a route. It is the default model of the routes." },
            { "route name", "Llm.Routes['nowhere']: 'nowhere' is not an upstream prompt name or group name." },
            { "route null", "Llm.Routes['dedupe_edges'] is null." },
            { "route model", "Llm.Routes['dedupe_edges'].ModelId must be a non-empty model id." },
            { "route max tokens", "Llm.Routes['dedupe_edges'].MaxTokens must be positive." },
            { "route override name", "Llm.Routes['dedupe_edges'].PromptOverrides['dedupe_edges.nope']: 'dedupe_edges.nope' is not one of the 25 upstream prompt names." },
            { "route override null", "Llm.Routes['dedupe_edges'].PromptOverrides['dedupe_edges.resolve_edge'] is null." },
            { "window", "ConcurrentIngestion: " },
            { "lease", "ConcurrentIngestion: " },
            { "write margin negative", "ConcurrentIngestion: " },
            { "write margin of the lease", "ConcurrentIngestion: " },
            { "poll interval", "ConcurrentIngestion: " },
            { "index lag", "ConcurrentIngestion: " },
            { "max apply attempts", "ConcurrentIngestion: " },
        };

    [Theory]
    [MemberData(nameof(ValidatorRules))]
    public void The_options_validator_fails_for_each_rule_with_one_message(string rule, string message)
    {
        var options = new GraphitiOptions();
        PromptBuilder keep = context => context.BuildDefault();
        switch (rule)
        {
            case "override name": options.Prompts.Overrides["extract_nodes.nope"] = keep; break;
            case "override null": options.Prompts.Overrides["extract_nodes.extract_text"] = null!; break;
            case "routes need a model": options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "m" }; break;
            case "routes need a non-blank model": options.Llm.Model = " "; options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "m" }; break;
            case "route name": options.Llm.Model = "m"; options.Llm.Routes["nowhere"] = new ModelRoute { ModelId = "m" }; break;
            case "route null": options.Llm.Model = "m"; options.Llm.Routes["dedupe_edges"] = null!; break;
            case "route model": options.Llm.Model = "m"; options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "" }; break;
            case "route max tokens": options.Llm.Model = "m"; options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "m", MaxTokens = -1 }; break;
            case "route override name":
                options.Llm.Model = "m";
                options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "m", PromptOverrides = { ["dedupe_edges.nope"] = keep } };
                break;
            case "route override null":
                options.Llm.Model = "m";
                options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "m", PromptOverrides = { ["dedupe_edges.resolve_edge"] = null! } };
                break;
            case "window": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 0 }; break;
            case "lease": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, Lease = TimeSpan.Zero }; break;
            case "write margin negative": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, WriteMargin = TimeSpan.FromSeconds(-1) }; break;
            case "write margin of the lease":
                options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, Lease = TimeSpan.FromSeconds(9), WriteMargin = TimeSpan.FromSeconds(3) };
                break;
            case "poll interval": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, PollInterval = TimeSpan.Zero }; break;
            case "index lag": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, IndexLag = TimeSpan.FromSeconds(-1) }; break;
            case "max apply attempts": options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 1, MaxApplyAttempts = 0 }; break;
            default: throw new ArgumentOutOfRangeException(nameof(rule), rule, null);
        }

        var result = new GraphitiOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.StartsWith(message, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_validator_passes_valid_options()
    {
        var options = new GraphitiOptions { ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 2 } };
        options.Llm.Model = "m";
        options.Llm.Routes["extract_edges"] = new ModelRoute { ModelId = "e", MaxTokens = 1, PromptOverrides = { ["extract_edges.edge"] = c => c.BuildDefault() } };
        options.Prompts.Overrides["extract_nodes.extract_text"] = c => c.BuildDefault();

        Assert.True(new GraphitiOptionsValidator().Validate(null, options).Succeeded);
    }

    internal static ScriptedChatClient Chat() => new((_, _) => "{}");

    internal static ServiceCollection Complete(FakeClient driver, Action<GraphitiOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(Chat());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        services.AddGraphiti(configure).AddNeo4j(_ => driver);
        return services;
    }

    private static Func<object> Resolve(IServiceProvider provider) => () => provider.GetRequiredService<IGraphiti>();

    private static void Spoil(GraphitiOptions options, string kind)
    {
        switch (kind)
        {
            case "route without model":
                options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "small" };
                break;
            case "unknown override":
                options.Prompts.Overrides["extract_nodes.nope"] = context => context.BuildDefault();
                break;
            case "unknown route":
                options.Llm.Model = "medium";
                options.Llm.Routes["nowhere"] = new ModelRoute { ModelId = "small" };
                break;
            case "empty route model":
                options.Llm.Model = "medium";
                options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = " " };
                break;
            case "route max tokens":
                options.Llm.Model = "medium";
                options.Llm.Routes["dedupe_edges"] = new ModelRoute { ModelId = "small", MaxTokens = 0 };
                break;
            case "window":
                options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = 0 };
                break;
            case "write margin":
                options.ConcurrentIngestion = new ConcurrentIngestionOptions
                {
                    Window = 2,
                    Lease = TimeSpan.FromSeconds(9),
                    WriteMargin = TimeSpan.FromSeconds(3),
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
