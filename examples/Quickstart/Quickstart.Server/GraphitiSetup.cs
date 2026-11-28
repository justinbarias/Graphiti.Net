namespace Quickstart;

using Graphiti;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

/// <summary>Registers Graphiti with the OpenAI models and the Neo4j driver.</summary>
internal static class GraphitiSetup
{
    /// <summary>Builds the chat client of the example. The agent and Graphiti both use the same model.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The chat client.</returns>
    public static IChatClient CreateChatClient(Settings settings) =>
        new StructuredOutputChatClient(
            new OpenAIClient(settings.OpenAiApiKey).GetChatClient(settings.ChatModel).AsIChatClient());

    /// <summary>Registers <c>IGraphiti</c> in the service collection.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="ingestWindow">The window of concurrent ingestion, or null for serial ingestion.</param>
    public static void AddQuickstartGraphiti(this IServiceCollection services, Settings settings, int? ingestWindow = null)
    {
        var openAi = new OpenAIClient(settings.OpenAiApiKey);
        services
            .AddGraphiti(options =>
            {
                options.Llm.Model = settings.ChatModel;
                options.Embedding.Model = settings.EmbeddingModel;
                if (ingestWindow is { } window)
                {
                    options.ConcurrentIngestion = new ConcurrentIngestionOptions { Window = window };
                }
            })
            .UseChatClient(_ => CreateChatClient(settings))
            .UseEmbeddingGenerator(_ => (IEmbeddingGenerator<string, Embedding<float>>)
                openAi.GetEmbeddingClient(settings.EmbeddingModel).AsIEmbeddingGenerator())
            .AddNeo4j(options =>
            {
                options.Uri = settings.Neo4jUri;
                options.User = settings.Neo4jUser;
                options.Password = settings.Neo4jPassword;
            });
    }
}
