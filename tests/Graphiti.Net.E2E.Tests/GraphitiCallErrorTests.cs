// The error that a host gets from a call of the public surface (plan 0004, layer 9, packet P9d, follow-up). With the
// tracer that the facade passes, an error in the body of a span becomes "generator didn't stop after throw()" (GAPS
// U19). The facade gives the host the error of the call, with and without a listener on the source Graphiti.Net.
// Port.Evaluation TracerTests shows U19 below the facade.
namespace Graphiti.E2E.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using Graphiti.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// An ActivityListener is process-wide, so these tests run alone.
[Collection(ProcessWideServicesGroup.Name)]
public sealed class GraphitiCallErrorTests
{
    private const int TimeLimit = 180_000;

    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory(Timeout = TimeLimit)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_episode_uuid_gives_NodeNotFoundException(bool listen)
    {
        var ct = TestContext.Current.CancellationToken;
        using var spans = SpanListener.Start(listen);
        await using var provider = GraphitiRegistrationTests.Complete(new FakeClient()).BuildServiceProvider();
        var graphiti = provider.GetRequiredService<IGraphiti>();
        var uuid = Guid.NewGuid().ToString();

        // The fake driver returns no record, so get_by_uuid raises NodeNotFoundError inside the span of add_episode.
        var error = await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January) with { Uuid = uuid }, new AddEpisodeOptions { GroupId = "g1" }, ct));

        Assert.Equal($"node {uuid} not found", error.Message);
        Assert.Null(error.InnerException);
        spans.AssertFailed("add_episode");
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_InvalidOperationException_of_the_host_arrives_as_it_is(bool listen)
    {
        var ct = TestContext.Current.CancellationToken;
        using var spans = SpanListener.Start(listen);
        var thrown = new InvalidOperationException("The prompt of the host failed.", new FormatException("The template is bad."));
        await using var provider = GraphitiRegistrationTests.Complete(
                new FakeClient(), o => o.Prompts.Overrides["extract_nodes.extract_text"] = _ => throw thrown)
            .BuildServiceProvider();
        var graphiti = provider.GetRequiredService<IGraphiti>();

        // The delegate runs inside the span of add_episode. Its error has an inner exception and another message, so it
        // is not a wrap.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = "g1" }, ct));

        Assert.Same(thrown, error);
        Assert.IsType<FormatException>(error.InnerException);
        spans.AssertFailed("add_episode");
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateCommunities_gives_the_ValueError_of_upstream_after_the_episode_is_saved(bool listen)
    {
        var ct = TestContext.Current.CancellationToken;
        using var spans = SpanListener.Start(listen);
        await using var scope = new GraphitiScope(GraphitiArgumentsTests.Solo);

        // GAPS U14: add_episode unpacks the list of results of update_community into two names. With one node it raises
        // ValueError, after the episode is saved.
        var error = await Assert.ThrowsAsync<ArgumentException>(() => scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, UpdateCommunities = true }, ct));

        Assert.Equal("not enough values to unpack (expected 2, got 1)", error.Message);
        Assert.Equal(1, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
        Assert.Equal(1, await scope.CountAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN count(n) AS c"));
        spans.AssertFailed("add_episode");
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Llm_ClassifyFailure_classifies_the_errors_of_the_default_reranker(bool listen)
    {
        var ct = TestContext.Current.CancellationToken;
        var hook = new FirstTokenTrue();
        await using var scope = new GraphitiScope(
            GraphitiMembersTests.Ana,
            o => o.Llm.ClassifyFailure = e => e is TimeoutException ? ModelCallFailure.RateLimit : ModelCallFailure.None,
            build: b => b.UseChatLogProbabilities(_ => hook));
        scope.Chat.Rerank = _ => throw new TimeoutException("the provider is slow");
        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
        await GraphitiModelOptionsTests.TwoFactsAsync(scope, ct);
        using var spans = SpanListener.Start(listen);

        // openai_reranker_client.py raises RateLimitError for a rate limit of the client. The classifier of the options
        // says that a TimeoutException is a rate limit.
        var error = await Assert.ThrowsAsync<RateLimitException>(() => scope.Graphiti.AdvancedSearchAsync(
            "Ana", SearchRecipes.EdgeHybridSearchCrossEncoder, new AdvancedSearchOptions { GroupIds = [scope.Group] }, ct));

        Assert.Equal("Rate limit exceeded. Please try again later.", error.Message);
        Assert.Equal("the provider is slow", Assert.IsType<TimeoutException>(error.InnerException).Message);
        spans.AssertFailed("search");
    }

    private sealed class FirstTokenTrue : IChatLogProbabilities
    {
        public void Request(ChatOptions options, int topLogProbabilityCount, IReadOnlyDictionary<int, int> logitBias)
        {
        }

        public IReadOnlyList<TokenLogProbability>? ReadFirstToken(ChatResponse response) => [new("True", 0)];
    }

    /// <summary>A listener on the source Graphiti.Net that keeps each span that stops, or no listener.</summary>
    internal sealed class SpanListener : IDisposable
    {
        private readonly ActivityListener? listener;
        private readonly ConcurrentQueue<Activity> stopped = new();

        /// <summary>Gets the name of each span that stopped.</summary>
        internal IReadOnlyList<string> Names => [.. stopped.Select(a => a.DisplayName)];

        private SpanListener(bool listen)
        {
            if (!listen)
            {
                Assert.False(GraphitiFacade.ActivitySource.HasListeners());
                return;
            }

            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Graphiti.Net",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            Assert.True(GraphitiFacade.ActivitySource.HasListeners());
        }

        internal static SpanListener Start(bool listen) => new(listen);

        /// <summary>With a listener, a span whose name contains <paramref name="name"/> stopped with the status error.</summary>
        internal void AssertFailed(string name)
        {
            if (listener is null)
            {
                Assert.Empty(stopped);
                return;
            }

            Assert.Contains(stopped, a => a.DisplayName.Contains(name, StringComparison.Ordinal) && a.Status == ActivityStatusCode.Error);
        }

        public void Dispose() => listener?.Dispose();
    }
}
