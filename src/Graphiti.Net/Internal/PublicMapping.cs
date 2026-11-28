namespace Graphiti.Internal;

using Graphiti.PyCompat;
using Core = Graphiti.Core;
using CoreSearch = Graphiti.Core.Search;
using CoreUtils = Graphiti.Core.Utils;

/// <summary>
/// The mapping between each public record and its ported class, in both directions (decision D25). A name without an
/// alias is the public type. <c>Core.X</c> and <c>CoreSearch.X</c> are the ported types. A ported object that the
/// mapping builds runs its <c>Validate()</c>, so a bad value throws the <c>ValidationException</c> of Python. An enum
/// maps by member, never by text.
/// </summary>
internal static class PublicMapping
{
    // ---------------------------------------------------------------------------------------------------------------
    // Nodes
    // ---------------------------------------------------------------------------------------------------------------

    internal static Core.EpisodicNode ToPorted(EpisodicNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Core.EpisodicNode
        {
            Uuid = node.Uuid,
            Name = node.Name,
            GroupId = node.GroupId,
            Labels = [.. node.Labels],
            CreatedAt = PublicValues.ToPortedDate(node.CreatedAt),
            Source = ToPorted(node.Source),
            SourceDescription = node.SourceDescription,
            Content = node.Content,
            ValidAt = PublicValues.ToPortedDate(node.ValidAt),
            EntityEdges = [.. node.EntityEdges],
            EpisodeMetadata = node.EpisodeMetadata is null ? null : PublicValues.ToPortedDict(node.EpisodeMetadata),
        }.Validate();
    }

    internal static EpisodicNode ToPublic(Core.EpisodicNode node) => new()
    {
        Uuid = node.Uuid,
        Name = node.Name,
        GroupId = node.GroupId,
        Labels = [.. node.Labels],
        CreatedAt = PublicValues.ToPublicDate(node.CreatedAt),
        Source = ToPublic(node.Source),
        SourceDescription = node.SourceDescription,
        Content = node.Content,
        ValidAt = PublicValues.ToPublicDate(node.ValidAt),
        EntityEdges = [.. node.EntityEdges],
        EpisodeMetadata = node.EpisodeMetadata is null ? null : PublicValues.ToPublicDict(node.EpisodeMetadata),
    };

    internal static Core.EntityNode ToPorted(EntityNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Core.EntityNode
        {
            Uuid = node.Uuid,
            Name = node.Name,
            GroupId = node.GroupId,
            Labels = [.. node.Labels],
            CreatedAt = PublicValues.ToPortedDate(node.CreatedAt),
            NameEmbedding = ToPortedVector(node.NameEmbedding),
            Summary = node.Summary,
            Attributes = PublicValues.ToPortedDict(node.Attributes),
        }.Validate();
    }

    internal static EntityNode ToPublic(Core.EntityNode node) => new()
    {
        Uuid = node.Uuid,
        Name = node.Name,
        GroupId = node.GroupId,
        Labels = [.. node.Labels],
        CreatedAt = PublicValues.ToPublicDate(node.CreatedAt),
        NameEmbedding = ToPublicVector(node.NameEmbedding),
        Summary = node.Summary,
        Attributes = PublicValues.ToPublicDict(node.Attributes),
    };

    internal static Core.CommunityNode ToPorted(CommunityNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Core.CommunityNode
        {
            Uuid = node.Uuid,
            Name = node.Name,
            GroupId = node.GroupId,
            Labels = [.. node.Labels],
            CreatedAt = PublicValues.ToPortedDate(node.CreatedAt),
            NameEmbedding = ToPortedVector(node.NameEmbedding),
            Summary = node.Summary,
        }.Validate();
    }

    internal static CommunityNode ToPublic(Core.CommunityNode node) => new()
    {
        Uuid = node.Uuid,
        Name = node.Name,
        GroupId = node.GroupId,
        Labels = [.. node.Labels],
        CreatedAt = PublicValues.ToPublicDate(node.CreatedAt),
        NameEmbedding = ToPublicVector(node.NameEmbedding),
        Summary = node.Summary,
    };

    internal static Core.SagaNode ToPorted(SagaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Core.SagaNode
        {
            Uuid = node.Uuid,
            Name = node.Name,
            GroupId = node.GroupId,
            Labels = [.. node.Labels],
            CreatedAt = PublicValues.ToPortedDate(node.CreatedAt),
            Summary = node.Summary,
            FirstEpisodeUuid = node.FirstEpisodeUuid,
            LastEpisodeUuid = node.LastEpisodeUuid,
            LastSummarizedAt = ToPortedDate(node.LastSummarizedAt),
            LastSummarizedEpisodeValidAt = ToPortedDate(node.LastSummarizedEpisodeValidAt),
        }.Validate();
    }

    internal static SagaNode ToPublic(Core.SagaNode node) => new()
    {
        Uuid = node.Uuid,
        Name = node.Name,
        GroupId = node.GroupId,
        Labels = [.. node.Labels],
        CreatedAt = PublicValues.ToPublicDate(node.CreatedAt),
        Summary = node.Summary,
        FirstEpisodeUuid = node.FirstEpisodeUuid,
        LastEpisodeUuid = node.LastEpisodeUuid,
        LastSummarizedAt = ToPublicDate(node.LastSummarizedAt),
        LastSummarizedEpisodeValidAt = ToPublicDate(node.LastSummarizedEpisodeValidAt),
    };

    internal static Core.EpisodeType ToPorted(EpisodeType value) => value switch
    {
        EpisodeType.Message => Core.EpisodeType.Message,
        EpisodeType.Json => Core.EpisodeType.Json,
        EpisodeType.Text => Core.EpisodeType.Text,
        EpisodeType.FactTriple => Core.EpisodeType.FactTriple,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EpisodeType."),
    };

    internal static EpisodeType ToPublic(Core.EpisodeType value) => value switch
    {
        Core.EpisodeType.Message => EpisodeType.Message,
        Core.EpisodeType.Json => EpisodeType.Json,
        Core.EpisodeType.Text => EpisodeType.Text,
        Core.EpisodeType.FactTriple => EpisodeType.FactTriple,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EpisodeType."),
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Edges
    // ---------------------------------------------------------------------------------------------------------------

    internal static Core.EntityEdge ToPorted(EntityEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new Core.EntityEdge
        {
            Uuid = edge.Uuid,
            GroupId = edge.GroupId,
            SourceNodeUuid = edge.SourceNodeUuid,
            TargetNodeUuid = edge.TargetNodeUuid,
            CreatedAt = PublicValues.ToPortedDate(edge.CreatedAt),
            Name = edge.Name,
            Fact = edge.Fact,
            FactEmbedding = ToPortedVector(edge.FactEmbedding),
            Episodes = [.. edge.Episodes],
            ExpiredAt = ToPortedDate(edge.ExpiredAt),
            ValidAt = ToPortedDate(edge.ValidAt),
            InvalidAt = ToPortedDate(edge.InvalidAt),
            ReferenceTime = ToPortedDate(edge.ReferenceTime),
            Attributes = PublicValues.ToPortedDict(edge.Attributes),
        }.Validate();
    }

    internal static EntityEdge ToPublic(Core.EntityEdge edge) => new()
    {
        Uuid = edge.Uuid,
        GroupId = edge.GroupId,
        SourceNodeUuid = edge.SourceNodeUuid,
        TargetNodeUuid = edge.TargetNodeUuid,
        CreatedAt = PublicValues.ToPublicDate(edge.CreatedAt),
        Name = edge.Name,
        Fact = edge.Fact,
        FactEmbedding = ToPublicVector(edge.FactEmbedding),
        Episodes = [.. edge.Episodes],
        ExpiredAt = ToPublicDate(edge.ExpiredAt),
        ValidAt = ToPublicDate(edge.ValidAt),
        InvalidAt = ToPublicDate(edge.InvalidAt),
        ReferenceTime = ToPublicDate(edge.ReferenceTime),
        Attributes = PublicValues.ToPublicDict(edge.Attributes),
    };

    internal static Core.EpisodicEdge ToPorted(EpisodicEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new Core.EpisodicEdge
        {
            Uuid = edge.Uuid,
            GroupId = edge.GroupId,
            SourceNodeUuid = edge.SourceNodeUuid,
            TargetNodeUuid = edge.TargetNodeUuid,
            CreatedAt = PublicValues.ToPortedDate(edge.CreatedAt),
        }.Validate();
    }

    internal static EpisodicEdge ToPublic(Core.EpisodicEdge edge) => new()
    {
        Uuid = edge.Uuid,
        GroupId = edge.GroupId,
        SourceNodeUuid = edge.SourceNodeUuid,
        TargetNodeUuid = edge.TargetNodeUuid,
        CreatedAt = PublicValues.ToPublicDate(edge.CreatedAt),
    };

    internal static Core.CommunityEdge ToPorted(CommunityEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new Core.CommunityEdge
        {
            Uuid = edge.Uuid,
            GroupId = edge.GroupId,
            SourceNodeUuid = edge.SourceNodeUuid,
            TargetNodeUuid = edge.TargetNodeUuid,
            CreatedAt = PublicValues.ToPortedDate(edge.CreatedAt),
        }.Validate();
    }

    internal static CommunityEdge ToPublic(Core.CommunityEdge edge) => new()
    {
        Uuid = edge.Uuid,
        GroupId = edge.GroupId,
        SourceNodeUuid = edge.SourceNodeUuid,
        TargetNodeUuid = edge.TargetNodeUuid,
        CreatedAt = PublicValues.ToPublicDate(edge.CreatedAt),
    };

    internal static Core.HasEpisodeEdge ToPorted(HasEpisodeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new Core.HasEpisodeEdge
        {
            Uuid = edge.Uuid,
            GroupId = edge.GroupId,
            SourceNodeUuid = edge.SourceNodeUuid,
            TargetNodeUuid = edge.TargetNodeUuid,
            CreatedAt = PublicValues.ToPortedDate(edge.CreatedAt),
        }.Validate();
    }

    internal static HasEpisodeEdge ToPublic(Core.HasEpisodeEdge edge) => new()
    {
        Uuid = edge.Uuid,
        GroupId = edge.GroupId,
        SourceNodeUuid = edge.SourceNodeUuid,
        TargetNodeUuid = edge.TargetNodeUuid,
        CreatedAt = PublicValues.ToPublicDate(edge.CreatedAt),
    };

    internal static Core.NextEpisodeEdge ToPorted(NextEpisodeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new Core.NextEpisodeEdge
        {
            Uuid = edge.Uuid,
            GroupId = edge.GroupId,
            SourceNodeUuid = edge.SourceNodeUuid,
            TargetNodeUuid = edge.TargetNodeUuid,
            CreatedAt = PublicValues.ToPortedDate(edge.CreatedAt),
        }.Validate();
    }

    internal static NextEpisodeEdge ToPublic(Core.NextEpisodeEdge edge) => new()
    {
        Uuid = edge.Uuid,
        GroupId = edge.GroupId,
        SourceNodeUuid = edge.SourceNodeUuid,
        TargetNodeUuid = edge.TargetNodeUuid,
        CreatedAt = PublicValues.ToPublicDate(edge.CreatedAt),
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Inputs and results
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>One <see cref="EpisodeInput"/> of a bulk call as the ported <c>RawEpisode</c>.</summary>
    internal static CoreUtils.RawEpisode ToRawEpisode(EpisodeInput episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new CoreUtils.RawEpisode
        {
            Name = episode.Name,
            Uuid = episode.Uuid,
            Content = episode.Content,
            SourceDescription = episode.SourceDescription,
            Source = ToPorted(episode.Source),
            ReferenceTime = PublicValues.ToPortedDate(episode.ReferenceTime),
        };
    }

    /// <summary>
    /// A list of custom types as upstream's <c>dict[str, type[BaseModel]]</c>. The list order is the dict order. A
    /// second definition with the same name keeps the place of the first and replaces its value, as a Python dict
    /// display does.
    /// </summary>
    internal static OrderedDictionary<string, GraphTypeDescriptor>? ToTypeMap(IReadOnlyList<GraphTypeDefinition>? types)
    {
        if (types is null)
        {
            return null;
        }

        var map = new OrderedDictionary<string, GraphTypeDescriptor>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type, nameof(types));
            map[type.Name] = type.ToDescriptor();
        }

        return map;
    }

    /// <summary>
    /// The rules of an edge type map as upstream's <c>dict[tuple[str, str], list[str]]</c>, in the order of the list. A
    /// second rule for one pair keeps the place of the first and replaces its value, as a Python dict display does.
    /// </summary>
    internal static OrderedDictionary<(string, string), List<string>>? ToEdgeTypeMap(IReadOnlyList<EdgeTypeRule>? rules)
    {
        if (rules is null)
        {
            return null;
        }

        var map = new OrderedDictionary<(string, string), List<string>>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
            map[(rule.SourceType, rule.TargetType)] = [.. rule.EdgeTypes];
        }

        return map;
    }

    /// <summary><c>identity_properties</c>: a dictionary in the order of the public dictionary.</summary>
    internal static OrderedDictionary<string, List<string>>? ToIdentityProperties(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var map = new OrderedDictionary<string, List<string>>(properties.Count, StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            map[key] = [.. value];
        }

        return map;
    }

    /// <summary>A list of strings, or null.</summary>
    internal static List<string>? ToPortedList(IReadOnlyList<string>? values) => values is null ? null : [.. values];

    /// <summary>
    /// <c>saga: str | SagaNode | None</c> from the two public properties. A call that sets both throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    internal static Core.SagaOrName? ToSaga(string? saga, SagaNode? sagaNode)
    {
        if (saga is not null && sagaNode is not null)
        {
            throw new ArgumentException("Set Saga or SagaNode, not both.");
        }

        if (saga is not null)
        {
            return new Core.SagaOrName.Name { Value = saga };
        }

        return sagaNode is null ? null : new Core.SagaOrName.Node { Saga = ToPorted(sagaNode) };
    }

    internal static AddEpisodeResult ToPublic(Core.AddEpisodeResults result) => new()
    {
        Episode = ToPublic(result.Episode),
        EpisodicEdges = [.. result.EpisodicEdges.Select(ToPublic)],
        Nodes = [.. result.Nodes.Select(ToPublic)],
        Edges = [.. result.Edges.Select(ToPublic)],
        Communities = [.. result.Communities.Select(ToPublic)],
        CommunityEdges = [.. result.CommunityEdges.Select(ToPublic)],
    };

    internal static AddEpisodeBulkResult ToPublic(Core.AddBulkEpisodeResults result) => new()
    {
        Episodes = [.. result.Episodes.Select(ToPublic)],
        EpisodicEdges = [.. result.EpisodicEdges.Select(ToPublic)],
        Nodes = [.. result.Nodes.Select(ToPublic)],
        Edges = [.. result.Edges.Select(ToPublic)],
        Communities = [.. result.Communities.Select(ToPublic)],
        CommunityEdges = [.. result.CommunityEdges.Select(ToPublic)],
    };

    internal static AddTripletResult ToPublic(Core.AddTripletResults result) => new()
    {
        Nodes = [.. result.Nodes.Select(ToPublic)],
        Edges = [.. result.Edges.Select(ToPublic)],
    };

    internal static BuildCommunitiesResult ToPublic((List<Core.CommunityNode> First, List<Core.CommunityEdge> Second) result) => new()
    {
        Communities = [.. result.First.Select(ToPublic)],
        CommunityEdges = [.. result.Second.Select(ToPublic)],
    };

    internal static SearchResults ToPublic(CoreSearch.SearchResults result) => new()
    {
        Edges = [.. result.Edges.Select(ToPublic)],
        EdgeRerankerScores = [.. result.EdgeRerankerScores],
        Nodes = [.. result.Nodes.Select(ToPublic)],
        NodeRerankerScores = [.. result.NodeRerankerScores],
        Episodes = [.. result.Episodes.Select(ToPublic)],
        EpisodeRerankerScores = [.. result.EpisodeRerankerScores],
        Communities = [.. result.Communities.Select(ToPublic)],
        CommunityRerankerScores = [.. result.CommunityRerankerScores],
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Search configuration and filters
    // ---------------------------------------------------------------------------------------------------------------

    internal static CoreSearch.SearchConfig ToPorted(SearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new CoreSearch.SearchConfig
        {
            EdgeConfig = config.EdgeConfig is null ? null : ToPorted(config.EdgeConfig),
            NodeConfig = config.NodeConfig is null ? null : ToPorted(config.NodeConfig),
            EpisodeConfig = config.EpisodeConfig is null ? null : ToPorted(config.EpisodeConfig),
            CommunityConfig = config.CommunityConfig is null ? null : ToPorted(config.CommunityConfig),
            Limit = config.Limit,
            RerankerMinScore = config.RerankerMinScore,
        }.Validate();
    }

    internal static CoreSearch.EdgeSearchConfig ToPorted(EdgeSearchConfig config) => new CoreSearch.EdgeSearchConfig
    {
        SearchMethods = [.. config.SearchMethods.Select(ToPorted)],
        Reranker = ToPorted(config.Reranker),
        SimMinScore = config.SimMinScore,
        MmrLambda = config.MmrLambda,
        BfsMaxDepth = config.BfsMaxDepth,
    }.Validate();

    internal static CoreSearch.NodeSearchConfig ToPorted(NodeSearchConfig config) => new CoreSearch.NodeSearchConfig
    {
        SearchMethods = [.. config.SearchMethods.Select(ToPorted)],
        Reranker = ToPorted(config.Reranker),
        SimMinScore = config.SimMinScore,
        MmrLambda = config.MmrLambda,
        BfsMaxDepth = config.BfsMaxDepth,
    }.Validate();

    internal static CoreSearch.EpisodeSearchConfig ToPorted(EpisodeSearchConfig config) => new CoreSearch.EpisodeSearchConfig
    {
        SearchMethods = [.. config.SearchMethods.Select(ToPorted)],
        Reranker = ToPorted(config.Reranker),
        SimMinScore = config.SimMinScore,
        MmrLambda = config.MmrLambda,
        BfsMaxDepth = config.BfsMaxDepth,
    }.Validate();

    internal static CoreSearch.CommunitySearchConfig ToPorted(CommunitySearchConfig config) => new CoreSearch.CommunitySearchConfig
    {
        SearchMethods = [.. config.SearchMethods.Select(ToPorted)],
        Reranker = ToPorted(config.Reranker),
        SimMinScore = config.SimMinScore,
        MmrLambda = config.MmrLambda,
        BfsMaxDepth = config.BfsMaxDepth,
    }.Validate();

    internal static CoreSearch.EdgeSearchMethod ToPorted(EdgeSearchMethod value) => value switch
    {
        EdgeSearchMethod.CosineSimilarity => CoreSearch.EdgeSearchMethod.CosineSimilarity,
        EdgeSearchMethod.Bm25 => CoreSearch.EdgeSearchMethod.Bm25,
        EdgeSearchMethod.Bfs => CoreSearch.EdgeSearchMethod.Bfs,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EdgeSearchMethod."),
    };

    internal static CoreSearch.NodeSearchMethod ToPorted(NodeSearchMethod value) => value switch
    {
        NodeSearchMethod.CosineSimilarity => CoreSearch.NodeSearchMethod.CosineSimilarity,
        NodeSearchMethod.Bm25 => CoreSearch.NodeSearchMethod.Bm25,
        NodeSearchMethod.Bfs => CoreSearch.NodeSearchMethod.Bfs,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of NodeSearchMethod."),
    };

    internal static CoreSearch.EpisodeSearchMethod ToPorted(EpisodeSearchMethod value) => value switch
    {
        EpisodeSearchMethod.Bm25 => CoreSearch.EpisodeSearchMethod.Bm25,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EpisodeSearchMethod."),
    };

    internal static CoreSearch.CommunitySearchMethod ToPorted(CommunitySearchMethod value) => value switch
    {
        CommunitySearchMethod.CosineSimilarity => CoreSearch.CommunitySearchMethod.CosineSimilarity,
        CommunitySearchMethod.Bm25 => CoreSearch.CommunitySearchMethod.Bm25,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of CommunitySearchMethod."),
    };

    internal static CoreSearch.EdgeReranker ToPorted(EdgeReranker value) => value switch
    {
        EdgeReranker.Rrf => CoreSearch.EdgeReranker.Rrf,
        EdgeReranker.NodeDistance => CoreSearch.EdgeReranker.NodeDistance,
        EdgeReranker.EpisodeMentions => CoreSearch.EdgeReranker.EpisodeMentions,
        EdgeReranker.Mmr => CoreSearch.EdgeReranker.Mmr,
        EdgeReranker.CrossEncoder => CoreSearch.EdgeReranker.CrossEncoder,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EdgeReranker."),
    };

    internal static CoreSearch.NodeReranker ToPorted(NodeReranker value) => value switch
    {
        NodeReranker.Rrf => CoreSearch.NodeReranker.Rrf,
        NodeReranker.NodeDistance => CoreSearch.NodeReranker.NodeDistance,
        NodeReranker.EpisodeMentions => CoreSearch.NodeReranker.EpisodeMentions,
        NodeReranker.Mmr => CoreSearch.NodeReranker.Mmr,
        NodeReranker.CrossEncoder => CoreSearch.NodeReranker.CrossEncoder,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of NodeReranker."),
    };

    internal static CoreSearch.EpisodeReranker ToPorted(EpisodeReranker value) => value switch
    {
        EpisodeReranker.Rrf => CoreSearch.EpisodeReranker.Rrf,
        EpisodeReranker.CrossEncoder => CoreSearch.EpisodeReranker.CrossEncoder,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of EpisodeReranker."),
    };

    internal static CoreSearch.CommunityReranker ToPorted(CommunityReranker value) => value switch
    {
        CommunityReranker.Rrf => CoreSearch.CommunityReranker.Rrf,
        CommunityReranker.Mmr => CoreSearch.CommunityReranker.Mmr,
        CommunityReranker.CrossEncoder => CoreSearch.CommunityReranker.CrossEncoder,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of CommunityReranker."),
    };

    internal static CoreSearch.SearchFilters? ToPorted(SearchFilters? filters) => filters is null ? null : new CoreSearch.SearchFilters
    {
        NodeLabels = ToPortedList(filters.NodeLabels),
        EdgeTypes = ToPortedList(filters.EdgeTypes),
        ValidAt = ToPorted(filters.ValidAt),
        InvalidAt = ToPorted(filters.InvalidAt),
        CreatedAt = ToPorted(filters.CreatedAt),
        ExpiredAt = ToPorted(filters.ExpiredAt),
        EdgeUuids = ToPortedList(filters.EdgeUuids),
        EdgeSourceNodeUuids = ToPortedList(filters.EdgeSourceNodeUuids),
        EdgeTargetNodeUuids = ToPortedList(filters.EdgeTargetNodeUuids),
        PropertyFilters = filters.PropertyFilters is null ? null : [.. filters.PropertyFilters.Select(ToPorted)],
    }.Validate();

    internal static List<List<CoreSearch.DateFilter>>? ToPorted(IReadOnlyList<IReadOnlyList<DateFilter>>? groups) =>
        groups is null ? null : [.. groups.Select(group => group.Select(ToPorted).ToList())];

    internal static CoreSearch.DateFilter ToPorted(DateFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new CoreSearch.DateFilter
        {
            Date = ToPortedDate(filter.Date),
            ComparisonOperator = ToPorted(filter.ComparisonOperator),
        }.Validate();
    }

    internal static CoreSearch.PropertyFilter ToPorted(PropertyFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new CoreSearch.PropertyFilter
        {
            PropertyName = filter.PropertyName,
            PropertyValue = filter.PropertyValue switch
            {
                null or string or long or double => filter.PropertyValue,
                _ => throw new InvalidCastException(
                    $"A property value of the type {filter.PropertyValue.GetType().FullName} has no form in Graphiti. "
                    + "A value is null, string, long, or double."),
            },
            ComparisonOperator = ToPorted(filter.ComparisonOperator),
        }.Validate();
    }

    internal static CoreSearch.ComparisonOperator ToPorted(ComparisonOperator value) => value switch
    {
        ComparisonOperator.Equals => CoreSearch.ComparisonOperator.Equals,
        ComparisonOperator.NotEquals => CoreSearch.ComparisonOperator.NotEquals,
        ComparisonOperator.GreaterThan => CoreSearch.ComparisonOperator.GreaterThan,
        ComparisonOperator.LessThan => CoreSearch.ComparisonOperator.LessThan,
        ComparisonOperator.GreaterThanEqual => CoreSearch.ComparisonOperator.GreaterThanEqual,
        ComparisonOperator.LessThanEqual => CoreSearch.ComparisonOperator.LessThanEqual,
        ComparisonOperator.IsNull => CoreSearch.ComparisonOperator.IsNull,
        ComparisonOperator.IsNotNull => CoreSearch.ComparisonOperator.IsNotNull,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a member of ComparisonOperator."),
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Values
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>An embedding as the ported <c>list[float]</c>. Each 64-bit value stays.</summary>
    internal static List<double>? ToPortedVector(ReadOnlyMemory<double>? vector) => vector is { } value ? [.. value.Span] : null;

    /// <summary>A ported embedding as a public vector. Each 64-bit value stays.</summary>
    internal static ReadOnlyMemory<double>? ToPublicVector(List<double>? vector) => vector is null ? null : new([.. vector]);

    private static PyDateTime? ToPortedDate(DateTimeOffset? value) => value is { } date ? PublicValues.ToPortedDate(date) : null;

    private static DateTimeOffset? ToPublicDate(PyDateTime? value) => value is { } date ? PublicValues.ToPublicDate(date) : null;
}
