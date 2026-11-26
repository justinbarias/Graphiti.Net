namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;

/// <summary>
/// Connection to the Neo4j sidecar from docker-compose.yml. Every end-to-end test class shares it.
/// Tests isolate their data with a unique group id, never with a fresh database.
/// </summary>
public sealed class Neo4jSidecar : IAsyncLifetime
{
    public static string Uri { get; } =
        Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687";

    public static string User { get; } =
        Environment.GetEnvironmentVariable("NEO4J_USER") ?? "neo4j";

    public static string Password { get; } =
        Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "password";

    public IDriver Driver { get; } = GraphDatabase.Driver(Uri, AuthTokens.Basic(User, Password));

    public async ValueTask InitializeAsync()
    {
        try
        {
            await Driver.VerifyConnectivityAsync();
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new InvalidOperationException(
                $"Neo4j is not usable at {Uri} as user '{User}'. Run `docker compose up -d --wait neo4j` " +
                "from the repository root, then run the tests again.",
                ex);
        }
    }

    public ValueTask DisposeAsync() => Driver.DisposeAsync();

    /// <summary>Returns a group id that no other test uses.</summary>
    public static string NewGroupId() => $"e2e_{Guid.NewGuid():N}";
}

[CollectionDefinition(Name)]
public sealed class Neo4jFixtureGroup : ICollectionFixture<Neo4jSidecar>
{
    public const string Name = "neo4j";
}
