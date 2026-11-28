// The effect of five public options (plan 0004, layer 9, packet P9d, adversarial pass, finding F1): TraceSpanPrefix,
// MaxCoroutines, Embedding.Model, Embedding.Dimensions, and ConcurrentIngestion.EnabledForGroup. Each test reads the
// effect at the network boundary: a span, a request of the chat client or of the embedding generator, or a stored row.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// The span listener is process-wide, so these tests run alone.
[Collection(ProcessWideServicesGroup.Name)]
public sealed class GraphitiOptionEffectsTests
{
    private const int TimeLimit = 180_000;

    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = TimeLimit)]
    public async Task TraceSpanPrefix_is_the_prefix_of_each_span_name()
    {
        var ct = TestContext.Current.CancellationToken;
        using var spans = GraphitiCallErrorTests.SpanListener.Start(listen: true);
        await using var provider = GraphitiRegistrationTests.Complete(new FakeClient(), o => o.TraceSpanPrefix = "host.trace.")
            .BuildServiceProvider();
        var graphiti = provider.GetRequiredService<IGraphiti>();

        // The fake driver returns no record, so add_episode fails inside its span. The span still stops.
        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January) with { Uuid = Guid.NewGuid().ToString() }, new AddEpisodeOptions { GroupId = "g1" }, ct));

        // OpenTelemetryTracer strips the trailing dot of the prefix and joins the name with one dot.
        Assert.Contains("host.trace.add_episode", spans.Names);
        Assert.DoesNotContain(spans.Names, n => n.StartsWith("graphiti.", StringComparison.Ordinal));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task MaxCoroutines_limits_the_model_calls_in_flight_of_a_bulk_call()
    {
        var ct = TestContext.Current.CancellationToken;

        // semaphore_gather admits max_coroutines calls at a time. The bulk call extracts the nodes of each episode with
        // one gather, so three episodes give three extraction calls at the same time with no small limit.
        Assert.Equal(1, await MaxInFlightAsync(1, ct));
        Assert.True(await MaxInFlightAsync(3, ct) > 1);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Embedding_Model_and_Dimensions_reach_the_generator_and_the_stored_vectors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, o =>
        {
            o.Embedding.Model = "embed-small";
            o.Embedding.Dimensions = 256;
        });

        await GraphitiMembersTests.AddAnaAsync(scope, ct);

        // The adapter sends the model id and no dimensions: upstream cuts each vector on the client.
        Assert.NotEmpty(scope.Embedder.Options);
        Assert.All(scope.Embedder.Options, o => Assert.Equal(("embed-small", (int?)null), (o?.ModelId, o?.Dimensions)));
        var sizes = await scope.QueryAsync(
            "MATCH (n:Entity) WHERE n.group_id = $g RETURN size(n.name_embedding) AS s UNION ALL " +
            "MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN size(r.fact_embedding) AS s");
        Assert.Equal(3, sizes.Count);
        Assert.All(sizes, r => Assert.Equal(256L, r["s"].As<long>()));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task EnabledForGroup_sends_only_the_accepted_group_through_the_scheduler()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), $"graphitinet-enabled-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "state.db");
        var other = Neo4jSidecar.NewGroupId();
        try
        {
            string? accepted = null;
            await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, o => o.ConcurrentIngestion = new ConcurrentIngestionOptions
            {
                Window = 2,
                StateStorePath = path,
                EnabledForGroup = group => group == accepted,
            });
            accepted = scope.Group;
            try
            {
                await scope.Graphiti.AddEpisodeAsync(GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group }, ct);
                await scope.Graphiti.AddEpisodeAsync(GraphitiMembersTests.Episode("b1", January), new AddEpisodeOptions { GroupId = other }, ct);

                // The scheduler records each unit in the state store. The episode of the other group took the plain path.
                Assert.Equal(["committed"], await GraphitiE8Tests.UnitStatusesAsync(path, scope.Group));
                Assert.Empty(await GraphitiE8Tests.UnitStatusesAsync(path, other));
                Assert.Equal(1, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $o RETURN count(n) AS c", new { o = other }));
            }
            finally
            {
                await scope.QueryAsync("MATCH (n) WHERE n.group_id = $o DETACH DELETE n", new { o = other });
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<int> MaxInFlightAsync(int maxCoroutines, CancellationToken ct)
    {
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, o => o.MaxCoroutines = maxCoroutines);
        scope.Chat.Delay = TimeSpan.FromMilliseconds(50);

        await scope.Graphiti.AddEpisodeBulkAsync(
            [GraphitiMembersTests.Episode("b1", January), GraphitiMembersTests.Episode("b2", January.AddDays(1)), GraphitiMembersTests.Episode("b3", January.AddDays(2))],
            new AddEpisodeBulkOptions { GroupId = scope.Group },
            ct);

        Assert.Equal(3, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
        return scope.Chat.MaxInFlight;
    }
}
