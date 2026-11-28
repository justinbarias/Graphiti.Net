namespace Graphiti.Internal;

using System.Runtime.ExceptionServices;
using Graphiti.PyCompat;
using Core = Graphiti.Core;
using CoreLlm = Graphiti.Core.LlmClient;

/// <summary>
/// The calls of <see cref="IGraphiti"/>. Each call maps its arguments, starts the <c>GraphitiCore</c> method of the same
/// name on the scheduler with <c>Asyncio.Run</c>, and maps the result there. The facade awaits with
/// <c>ConfigureAwait(false)</c>, so the task of a public call completes on the thread pool.
/// </summary>
internal sealed partial class GraphitiFacade : IGraphiti
{
    private TokenUsageTrackerFacade? tokenUsage;
    private NodeNamespacesFacade? nodes;
    private EdgeNamespacesFacade? edges;

    public ITokenUsageTracker TokenUsage => tokenUsage ??= new TokenUsageTrackerFacade(core.TokenTracker);

    public INodeNamespaces Nodes => nodes ??= new NodeNamespacesFacade(core);

    public IEdgeNamespaces Edges => edges ??= new EdgeNamespacesFacade(core);

    public Task<AddEpisodeResult> AddEpisodeAsync(
        EpisodeInput episode, AddEpisodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var o = options ?? new AddEpisodeOptions();
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.AddEpisodeAsync(
                episode.Name,
                episode.Content,
                episode.SourceDescription,
                PublicValues.ToPortedDate(episode.ReferenceTime),
                source: PublicMapping.ToPorted(episode.Source),
                groupId: o.GroupId,
                uuid: episode.Uuid,
                updateCommunities: o.UpdateCommunities,
                entityTypes: PublicMapping.ToTypeMap(o.EntityTypes),
                excludedEntityTypes: PublicMapping.ToPortedList(o.ExcludedEntityTypes),
                previousEpisodeUuids: PublicMapping.ToPortedList(o.PreviousEpisodeUuids),
                edgeTypes: PublicMapping.ToTypeMap(o.EdgeTypes),
                edgeTypeMap: PublicMapping.ToEdgeTypeMap(o.EdgeTypeMap),
                customExtractionInstructions: o.CustomExtractionInstructions,
                saga: PublicMapping.ToSaga(o.Saga, o.SagaNode),
                sagaPreviousEpisodeUuid: o.SagaPreviousEpisodeUuid,
                identityProperties: PublicMapping.ToIdentityProperties(o.IdentityProperties),
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<AddEpisodeBulkResult> AddEpisodeBulkAsync(
        IReadOnlyList<EpisodeInput> episodes, AddEpisodeBulkOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var o = options ?? new AddEpisodeBulkOptions();
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.AddEpisodeBulkAsync(
                [.. episodes.Select(PublicMapping.ToRawEpisode)],
                groupId: o.GroupId,
                entityTypes: PublicMapping.ToTypeMap(o.EntityTypes),
                excludedEntityTypes: PublicMapping.ToPortedList(o.ExcludedEntityTypes),
                edgeTypes: PublicMapping.ToTypeMap(o.EdgeTypes),
                edgeTypeMap: PublicMapping.ToEdgeTypeMap(o.EdgeTypeMap),
                strictEdgeTypes: o.StrictEdgeTypes,
                customExtractionInstructions: o.CustomExtractionInstructions,
                saga: PublicMapping.ToSaga(o.Saga, o.SagaNode),
                useCombinedExtraction: o.UseCombinedExtraction,
                identityProperties: PublicMapping.ToIdentityProperties(o.IdentityProperties),
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<AddTripletResult> AddTripletAsync(
        EntityNode source,
        EntityEdge edge,
        EntityNode target,
        AddTripletOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(target);
        var o = options ?? new AddTripletOptions();
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.AddTripletAsync(
                PublicMapping.ToPorted(source),
                PublicMapping.ToPorted(edge),
                PublicMapping.ToPorted(target),
                episode: o.Episode is null ? null : PublicMapping.ToPorted(o.Episode),
                identityProperties: PublicMapping.ToIdentityProperties(o.IdentityProperties),
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var o = options ?? new SearchOptions();
        return RunAsync<IReadOnlyList<EntityEdge>>(
            async () => [.. (await core.SearchAsync(
                query,
                centerNodeUuid: o.CenterNodeUuid,
                groupIds: PublicMapping.ToPortedList(o.GroupIds),
                numResults: o.NumResults,
                searchFilter: PublicMapping.ToPorted(o.SearchFilter),
                cancellationToken: cancellationToken)).Select(PublicMapping.ToPublic)],
            cancellationToken);
    }

    public Task<SearchResults> AdvancedSearchAsync(
        string query, SearchConfig config, AdvancedSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(config);
        var o = options ?? new AdvancedSearchOptions();
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.AdvancedSearchAsync(
                query,
                config: PublicMapping.ToPorted(config),
                groupIds: PublicMapping.ToPortedList(o.GroupIds),
                centerNodeUuid: o.CenterNodeUuid,
                bfsOriginNodeUuids: PublicMapping.ToPortedList(o.BfsOriginNodeUuids),
                searchFilter: PublicMapping.ToPorted(o.SearchFilter),
                queryVector: PublicMapping.ToPortedVector(o.QueryVector),
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTimeOffset referenceTime, RetrieveEpisodesOptions? options = null, CancellationToken cancellationToken = default)
    {
        var o = options ?? new RetrieveEpisodesOptions();
        return RunAsync<IReadOnlyList<EpisodicNode>>(
            async () => [.. (await core.RetrieveEpisodesAsync(
                PublicValues.ToPortedDate(referenceTime),
                lastN: o.LastN,
                groupIds: PublicMapping.ToPortedList(o.GroupIds),
                source: o.Source is { } source ? PublicMapping.ToPorted(source) : null,
                saga: o.Saga,
                cancellationToken: cancellationToken)).Select(PublicMapping.ToPublic)],
            cancellationToken);
    }

    public Task<SearchResults> GetNodesAndEdgesByEpisodeAsync(
        IReadOnlyList<string> episodeUuids, string? groupId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodeUuids);
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.GetNodesAndEdgesByEpisodeAsync(
                [.. episodeUuids], groupId, cancellationToken)),
            cancellationToken);
    }

    public Task RemoveEpisodeAsync(string episodeUuid, string? groupId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodeUuid);
        return RunAsync(() => core.RemoveEpisodeAsync(episodeUuid, groupId, cancellationToken), cancellationToken);
    }

    public Task<BuildCommunitiesResult> BuildCommunitiesAsync(
        IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () => PublicMapping.ToPublic(await core.BuildCommunitiesAsync(
                PublicMapping.ToPortedList(groupIds), cancellationToken: cancellationToken)),
            cancellationToken);

    public Task<SagaNode> SummarizeSagaAsync(string sagaId, string? groupId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaId);
        return RunAsync(
            async () => PublicMapping.ToPublic(await core.SummarizeSagaAsync(sagaId, groupId, cancellationToken)),
            cancellationToken);
    }

    public Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        RunAsync(() => core.BuildIndicesAndConstraintsAsync(deleteExisting, cancellationToken), cancellationToken);

    /// <summary>
    /// Runs <paramref name="call"/> on the scheduler and awaits it with <c>ConfigureAwait(false)</c>. A canceled token
    /// throws before the call starts. The host gets the error of the call where a span wrapped it.
    /// </summary>
    internal static async Task<T> RunAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await Asyncio.Run(call).ConfigureAwait(false);
        }
        catch (InvalidOperationException wrap) when (IsSpanWrap(wrap))
        {
            ExceptionDispatchInfo.Capture(ErrorOfTheCall(wrap)).Throw();
            throw;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{Task{T}}, CancellationToken)"/>
    internal static async Task RunAsync(Func<Task> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await Asyncio.Run(call).ConfigureAwait(false);
        }
        catch (InvalidOperationException wrap) when (IsSpanWrap(wrap))
        {
            ExceptionDispatchInfo.Capture(ErrorOfTheCall(wrap)).Throw();
            throw;
        }
    }

    // PORT: upstream defect U19. With an OpenTelemetry tracer, an error in the body of a span leaves the span as
    // "generator didn't stop after throw()", with the error of the body as the inner exception, and the ported tracer
    // keeps that (decision D45). The facade always passes a tracer (lead choice L9), so each error in a span would reach
    // the host in that form. The table "Public exception" of the public surface design names the error of the call, as
    // the no-op tracer of upstream gives it. The facade follows the inner exceptions of each nested wrap to that error.
    private static bool IsSpanWrap(Exception error) =>
        error is InvalidOperationException { Message: Core.OpenTelemetryTracer.GeneratorDidNotStopAfterThrow, InnerException: not null };

    private static Exception ErrorOfTheCall(Exception wrap)
    {
        var error = wrap;
        while (IsSpanWrap(error))
        {
            error = error.InnerException!;
        }

        return error;
    }
}

/// <summary>The public form of the ported <c>TokenUsageTracker</c>. The ported tracker holds a lock, so a host thread reads it.</summary>
internal sealed class TokenUsageTrackerFacade(CoreLlm.TokenUsageTracker tracker) : ITokenUsageTracker
{
    public IReadOnlyDictionary<string, PromptTokenUsage> GetUsage()
    {
        var usage = new OrderedDictionary<string, PromptTokenUsage>(StringComparer.Ordinal);
        foreach (var (name, value) in tracker.GetUsage())
        {
            usage[name] = new PromptTokenUsage
            {
                PromptName = value.PromptName,
                CallCount = value.CallCount,
                TotalInputTokens = value.TotalInputTokens,
                TotalOutputTokens = value.TotalOutputTokens,
            };
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, PromptTokenUsage>(usage);
    }

    public TokenUsage GetTotalUsage()
    {
        var total = tracker.GetTotalUsage();
        return new TokenUsage { InputTokens = total.InputTokens, OutputTokens = total.OutputTokens };
    }

    public void Reset() => tracker.Reset();
}
