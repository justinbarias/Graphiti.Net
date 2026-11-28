// One IGraphiti from a real service collection, with AddGraphiti and AddNeo4j on the shared Neo4j, the scripted chat
// client, and the hash embedding generator. Each scope has its own group id and deletes the nodes of that group, and
// only of that group, at the end.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

internal sealed class GraphitiScope : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IDriver raw;

    internal GraphitiScope(
        Func<string, string, string?> script,
        Action<GraphitiOptions>? configure = null,
        Action<IGraphitiBuilder>? build = null,
        Action<IServiceCollection>? register = null)
    {
        Chat = new ScriptedChatClient(script);
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(Chat);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Embedder);
        register?.Invoke(services);
        var builder = services.AddGraphiti(configure).AddNeo4j(o =>
        {
            o.Uri = Neo4jSidecar.Uri;
            o.User = Neo4jSidecar.User;
            o.Password = Neo4jSidecar.Password;
        });
        build?.Invoke(builder);
        provider = services.BuildServiceProvider();
        raw = GraphDatabase.Driver(Neo4jSidecar.Uri, AuthTokens.Basic(Neo4jSidecar.User, Neo4jSidecar.Password));
    }

    internal string Group { get; } = Neo4jSidecar.NewGroupId();

    internal ScriptedChatClient Chat { get; }

    internal HashEmbeddingGenerator Embedder { get; } = new();

    internal IGraphiti Graphiti => provider.GetRequiredService<IGraphiti>();

    /// <summary>Runs a query of the test on the raw driver, with the group id as <c>$g</c>.</summary>
    internal async Task<IReadOnlyList<IRecord>> QueryAsync(string cypher, object? parameters = null)
    {
        var values = new Dictionary<string, object?> { ["g"] = Group };
        if (parameters is not null)
        {
            foreach (var property in parameters.GetType().GetProperties())
            {
                values[property.Name] = property.GetValue(parameters);
            }
        }

        return (await raw.ExecutableQuery(cypher).WithParameters(values).ExecuteAsync()).Result;
    }

    /// <summary>The count of the rows of a query that returns one value <c>c</c>.</summary>
    internal async Task<long> CountAsync(string cypher, object? parameters = null) =>
        (await QueryAsync(cypher, parameters))[0]["c"].As<long>();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await raw.ExecutableQuery("MATCH (n) WHERE n.group_id = $g DETACH DELETE n")
                .WithParameters(new Dictionary<string, object> { ["g"] = Group })
                .ExecuteAsync();
        }
        finally
        {
            await raw.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
