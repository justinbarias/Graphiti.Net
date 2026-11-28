// The lifetime of the facade (plan 0004, layer 9, packet P9d, fix round): the process-wide services of two containers,
// the database of a host driver, the sync dispose on the Graphiti scheduler, and the cleanup after a failed build.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[Collection(ProcessWideServicesGroup.Name)]
public sealed class GraphitiLifetimeTests
{
    private const int TimeLimit = 180_000;

    [Fact(Timeout = TimeLimit)]
    public async Task A_disposed_second_container_leaves_its_services_to_the_first_container()
    {
        var ct = TestContext.Current.CancellationToken;
        var firstLines = new LineSink();
        var secondLines = new LineSink();
        var firstShift = TimeSpan.FromDays(3650);
        await using var first = new GraphitiScope(GraphitiMembersTests.Ana, register: s => s
            .AddLogging(b => b.AddProvider(firstLines).SetMinimumLevel(LogLevel.Information))
            .AddSingleton<TimeProvider>(new ShiftedClock(firstShift)));
        var second = new GraphitiScope(GraphitiMembersTests.Ana, register: s => s
            .AddLogging(b => b.AddProvider(secondLines).SetMinimumLevel(LogLevel.Information))
            .AddSingleton<TimeProvider>(new ShiftedClock(TimeSpan.FromDays(7300))));
        _ = first.Graphiti;
        _ = second.Graphiti;
        await second.DisposeAsync();

        var before = DateTimeOffset.UtcNow + firstShift;
        var added = await GraphitiMembersTests.AddAnaAsync(first, ct);
        var after = DateTimeOffset.UtcNow + firstShift;

        Assert.Contains(firstLines.Lines, line => line.StartsWith("Completed add_episode in ", StringComparison.Ordinal));
        Assert.Empty(secondLines.Lines);
        var created = (await first.QueryAsync("MATCH (n:Episodic {uuid: $u}) RETURN n.created_at AS t", new { u = added.Episode.Uuid }))
            .Single()["t"].As<ZonedDateTime>().ToDateTimeOffset();
        Assert.InRange(created, before.AddMilliseconds(-1), after.AddMilliseconds(1));
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_host_driver_uses_the_database_of_the_Neo4j_options(bool hostDriver)
    {
        var ct = TestContext.Current.CancellationToken;
        var database = $"graphitinet-missing-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(GraphitiRegistrationTests.Chat());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        if (hostDriver)
        {
            services.AddGraphiti().AddNeo4j(_ => GraphDatabase.Driver(Neo4jSidecar.Uri, AuthTokens.Basic(Neo4jSidecar.User, Neo4jSidecar.Password)));
            services.Configure<Neo4jOptions>(o => o.Database = database);
        }
        else
        {
            services.AddGraphiti().AddNeo4j(o =>
            {
                o.Uri = Neo4jSidecar.Uri;
                o.User = Neo4jSidecar.User;
                o.Password = Neo4jSidecar.Password;
                o.Database = database;
            });
        }

        await using var provider = services.BuildServiceProvider();
        var graphiti = provider.GetRequiredService<IGraphiti>();

        // The driver raises the server error as FatalDiscoveryException, a ClientException.
        var error = await Assert.ThrowsAnyAsync<ClientException>(
            () => graphiti.Nodes.Entity.GetByGroupIdsAsync([Neo4jSidecar.NewGroupId()], cancellationToken: ct));

        Assert.Equal("Neo.ClientError.Database.DatabaseNotFound", error.Code);
        Assert.Contains(database, error.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_sync_dispose_on_the_Graphiti_scheduler_throws_and_names_DisposeAsync()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var driver = new FakeClient();
        await using var provider = GraphitiRegistrationTests.Complete(driver).BuildServiceProvider();
        var graphiti = (IDisposable)provider.GetRequiredService<IGraphiti>();

        var error = await Asyncio.Run(() =>
            Task.FromResult(Assert.Throws<InvalidOperationException>(graphiti.Dispose)));

        Assert.Equal(GraphitiFacade.DisposeOnSchedulerMessage, error.Message);
        Assert.Contains("DisposeAsync", error.Message, StringComparison.Ordinal);
        Assert.Empty(driver.Calls.Names);

        // The refused dispose did not close Graphiti: a dispose off the scheduler closes the driver one time.
        graphiti.Dispose();
        Assert.Equal(["client.close"], driver.Calls.Names);
    }

    [Fact]
    public void A_failed_build_closes_the_driver_and_keeps_the_first_error()
    {
        var driver = new FakeClient { CloseError = new InvalidOperationException("the close failed") };
        var folder = Path.Combine(Path.GetTempPath(), $"graphitinet-{Guid.NewGuid():N}");
        var services = GraphitiRegistrationTests.Complete(driver, o => o.ConcurrentIngestion = new ConcurrentIngestionOptions
        {
            Window = 1,
            StateStorePath = Path.Combine(folder, "no-such-folder", "state.db"),
        });
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<SqliteException>(() => provider.GetRequiredService<IGraphiti>());

        Assert.Equal(14, error.SqliteErrorCode);
        Assert.Equal(["client.close"], driver.Calls.Names);
        Assert.False(Directory.Exists(folder));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_second_container_in_one_process_has_its_own_graph_clients_and_usage()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new GraphitiScope(GraphitiMembersTests.Ana);
        await using var second = new GraphitiScope(GraphitiMembersTests.Ana);
        try
        {
            var a = await GraphitiMembersTests.AddAnaAsync(first, ct);
            var b = await GraphitiMembersTests.AddAnaAsync(second, ct);

            Assert.NotSame(first.Graphiti, second.Graphiti);
            Assert.Equal(first.Chat.Calls.Count, first.Graphiti.TokenUsage.GetUsage().Values.Sum(u => u.CallCount));
            Assert.Equal(second.Chat.Calls.Count, second.Graphiti.TokenUsage.GetUsage().Values.Sum(u => u.CallCount));
            Assert.Equal([a.Episode.Uuid], await GraphitiNamespaceMembersTests.UuidsAsync(first, "MATCH (n:Episodic) WHERE n.group_id = $g RETURN n.uuid AS u"));
            Assert.Equal([b.Episode.Uuid], await GraphitiNamespaceMembersTests.UuidsAsync(second, "MATCH (n:Episodic) WHERE n.group_id = $g RETURN n.uuid AS u"));
        }
        finally
        {
            await first.DisposeAsync();
        }

        // The second container still works after the first one is disposed.
        var calls = second.Chat.Calls.Count;
        await second.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a2", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)), new AddEpisodeOptions { GroupId = second.Group }, ct);
        Assert.True(second.Chat.Calls.Count > calls);
        Assert.Equal(2, await second.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
    }

    /// <summary>A clock that runs at the system rate with a fixed shift.</summary>
    internal sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + shift;
    }

    /// <summary>A logger provider of the host that keeps each line that it receives.</summary>
    internal sealed class LineSink : ILoggerProvider
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Sink(Lines);

        public void Dispose()
        {
        }

        private sealed class Sink(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}

/// <summary>
/// The tests that read the process-wide services of ported code (decision D23). Each other class can build a facade,
/// which sets them, so this collection runs alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideServicesGroup
{
    public const string Name = "Process-wide services";
}
