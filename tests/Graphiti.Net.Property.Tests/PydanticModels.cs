namespace Graphiti.PyCompat.Tests.Models;

using System.Text.RegularExpressions;
using Graphiti.PyCompat;

// Test-only copies of upstream models, each written as RULES.md R7 tells an implementer to write a ported model:
// properties in field order, Validate(), ModelValidate(data), ModelDump(json), and ModelFields. The tests compare
// them with the real upstream models under pydantic. They prove that the Pydantic helpers are enough for R7.

/// <summary>The test registry: <c>M.ModelValidate</c> by the Python class name.</summary>
internal static class TestModels
{
    internal static readonly Dictionary<string, Func<OrderedDictionary<string, object?>, IPydanticModel>> ByName = new()
    {
        ["DateFilter"] = DateFilter.ModelValidate,
        ["PropertyFilter"] = PropertyFilter.ModelValidate,
        ["SearchFilters"] = SearchFilters.ModelValidate,
        ["EntityNode"] = EntityNode.ModelValidate,
        ["EpisodicNode"] = EpisodicNode.ModelValidate,
        ["NodeDuplicate"] = NodeDuplicate.ModelValidate,
        ["NodeResolutions"] = NodeResolutions.ModelValidate,
        ["Edge"] = Edge.ModelValidate,
        ["ExtractedEdges"] = ExtractedEdges.ModelValidate,
        ["SystemMessage"] = SystemMessage.ModelValidate,
        ["UserMessage"] = UserMessage.ModelValidate,
        ["ChatPrompt"] = ChatPrompt.ModelValidate,
        ["EdgeSearchConfig"] = EdgeSearchConfig.ModelValidate,
        ["NodeSearchConfig"] = NodeSearchConfig.ModelValidate,
        ["SearchConfig"] = SearchConfig.ModelValidate,
        ["ConcurrentIngestionConfig"] = ConcurrentIngestionConfig.ModelValidate,
        ["Ops"] = Ops.ModelValidate,
    };

    internal static readonly Dictionary<string, OrderedDictionary<string, PydanticField>> Fields = new()
    {
        ["DateFilter"] = DateFilter.ModelFields,
        ["PropertyFilter"] = PropertyFilter.ModelFields,
        ["SearchFilters"] = SearchFilters.ModelFields,
        ["EntityNode"] = EntityNode.ModelFields,
        ["EpisodicNode"] = EpisodicNode.ModelFields,
        ["NodeDuplicate"] = NodeDuplicate.ModelFields,
        ["NodeResolutions"] = NodeResolutions.ModelFields,
        ["Edge"] = Edge.ModelFields,
        ["ExtractedEdges"] = ExtractedEdges.ModelFields,
        ["SystemMessage"] = SystemMessage.ModelFields,
        ["UserMessage"] = UserMessage.ModelFields,
        ["ChatPrompt"] = ChatPrompt.ModelFields,
        ["EdgeSearchConfig"] = EdgeSearchConfig.ModelFields,
        ["NodeSearchConfig"] = NodeSearchConfig.ModelFields,
        ["SearchConfig"] = SearchConfig.ModelFields,
        ["ConcurrentIngestionConfig"] = ConcurrentIngestionConfig.ModelFields,
        ["Ops"] = Ops.ModelFields,
    };

    internal static IPydanticModel Validate(string name, OrderedDictionary<string, object?> data) => ByName[name](data);
}

/// <summary>A stand-in for the core class <c>NodeLabelValidationException</c>, which is not an <see cref="ArgumentException"/> (R4).</summary>
internal sealed class NodeLabelValidationException(string message) : Exception(message);

/// <summary>A stand-in for the core interface of <c>IngestionStateStore</c>.</summary>
internal interface IIngestionStateStore;

/// <summary>A stand-in for <c>helpers.validate_node_labels</c>, with the message of <c>NodeLabelValidationError</c>.</summary>
internal static partial class TestHelpers
{
    internal static bool ValidateNodeLabels(List<string>? nodeLabels)
    {
        if (nodeLabels is null || nodeLabels.Count == 0)
        {
            return true;
        }

        var invalid = nodeLabels.Where(label => !SafeCypherIdentifier().IsMatch(label)).ToList();
        if (invalid.Count > 0)
        {
            throw new NodeLabelValidationException(
                "node_labels must start with a letter or underscore and contain only alphanumeric characters or underscores: "
                + string.Join(", ", invalid.Select(label => $"\"{label}\"")));
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCypherIdentifier();
}

/// <summary>Copy of <c>search/search_filters.py</c> <c>DateFilter</c>.</summary>
internal sealed record DateFilter : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["date"] = new(false),
        ["comparison_operator"] = new(true),
    };

    internal PyDateTime? Date { get; set; }

    internal required ComparisonOperator ComparisonOperator { get; set; }

    internal DateFilter Validate() => this;

    internal static DateFilter ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("DateFilter", data);
        var date = f.Default("date", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var comparisonOperator = f.Required("comparison_operator", Pydantic.Enum<ComparisonOperator>);
        f.ThrowIfAny();
        return new DateFilter { Date = date.Value, ComparisonOperator = comparisonOperator.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("date", Date), ("comparison_operator", ComparisonOperator));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>search/search_filters.py</c> <c>PropertyFilter</c>.</summary>
internal sealed record PropertyFilter : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["property_name"] = new(true),
        ["property_value"] = new(false),
        ["comparison_operator"] = new(true),
    };

    internal required string PropertyName { get; set; }

    internal object? PropertyValue { get; set; }

    internal required ComparisonOperator ComparisonOperator { get; set; }

    internal PropertyFilter Validate() => this;

    internal static PropertyFilter ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("PropertyFilter", data);
        var propertyName = f.Required("property_name", Pydantic.Str);
        var propertyValue = f.Default("property_value", v => Pydantic.Optional(v, Pydantic.StrIntFloat), () => null);
        var comparisonOperator = f.Required("comparison_operator", Pydantic.Enum<ComparisonOperator>);
        f.ThrowIfAny();
        return new PropertyFilter
        {
            PropertyName = propertyName.Value,
            PropertyValue = propertyValue.Value,
            ComparisonOperator = comparisonOperator.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("property_name", PropertyName), ("property_value", PropertyValue), ("comparison_operator", ComparisonOperator));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>search/search_filters.py</c> <c>SearchFilters</c>, with its field validator.</summary>
internal sealed record SearchFilters : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["node_labels"] = new(false),
        ["edge_types"] = new(false),
        ["valid_at"] = new(false),
        ["invalid_at"] = new(false),
        ["created_at"] = new(false),
        ["expired_at"] = new(false),
        ["edge_uuids"] = new(false),
        ["edge_source_node_uuids"] = new(false),
        ["edge_target_node_uuids"] = new(false),
        ["property_filters"] = new(false),
    };

    internal List<string>? NodeLabels { get; set; }

    internal List<string>? EdgeTypes { get; set; }

    internal List<List<DateFilter>>? ValidAt { get; set; }

    internal List<List<DateFilter>>? InvalidAt { get; set; }

    internal List<List<DateFilter>>? CreatedAt { get; set; }

    internal List<List<DateFilter>>? ExpiredAt { get; set; }

    internal List<string>? EdgeUuids { get; set; }

    internal List<string>? EdgeSourceNodeUuids { get; set; }

    internal List<string>? EdgeTargetNodeUuids { get; set; }

    internal List<PropertyFilter>? PropertyFilters { get; set; }

    internal SearchFilters Validate()
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => TestHelpers.ValidateNodeLabels(NodeLabels), "SearchFilters", "node_labels");
        return this;
    }

    internal static SearchFilters ModelValidate(OrderedDictionary<string, object?> data)
    {
        static List<List<DateFilter>>? DateFilters(object? v) =>
            Pydantic.Optional(v, x => Pydantic.List(x, y => Pydantic.List(y, z => Pydantic.Model(z, "DateFilter", DateFilter.ModelValidate))));
        static List<string>? Strs(object? v) => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Str));

        var f = new PydanticFields("SearchFilters", data);
        var nodeLabels = f.Default("node_labels", Strs, () => null);
        var edgeTypes = f.Default("edge_types", Strs, () => null);
        var validAt = f.Default("valid_at", DateFilters, () => null);
        var invalidAt = f.Default("invalid_at", DateFilters, () => null);
        var createdAt = f.Default("created_at", DateFilters, () => null);
        var expiredAt = f.Default("expired_at", DateFilters, () => null);
        var edgeUuids = f.Default("edge_uuids", Strs, () => null);
        var edgeSourceNodeUuids = f.Default("edge_source_node_uuids", Strs, () => null);
        var edgeTargetNodeUuids = f.Default("edge_target_node_uuids", Strs, () => null);
        var propertyFilters = f.Default(
            "property_filters", v => Pydantic.Optional(v, x => Pydantic.List(x, y => Pydantic.Model(y, "PropertyFilter", PropertyFilter.ModelValidate))), () => null);
        f.ThrowIfAny();
        return new SearchFilters
        {
            NodeLabels = nodeLabels.Value,
            EdgeTypes = edgeTypes.Value,
            ValidAt = validAt.Value,
            InvalidAt = invalidAt.Value,
            CreatedAt = createdAt.Value,
            ExpiredAt = expiredAt.Value,
            EdgeUuids = edgeUuids.Value,
            EdgeSourceNodeUuids = edgeSourceNodeUuids.Value,
            EdgeTargetNodeUuids = edgeTargetNodeUuids.Value,
            PropertyFilters = propertyFilters.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("node_labels", NodeLabels),
        ("edge_types", EdgeTypes),
        ("valid_at", ValidAt),
        ("invalid_at", InvalidAt),
        ("created_at", CreatedAt),
        ("expired_at", ExpiredAt),
        ("edge_uuids", EdgeUuids),
        ("edge_source_node_uuids", EdgeSourceNodeUuids),
        ("edge_target_node_uuids", EdgeTargetNodeUuids),
        ("property_filters", PropertyFilters));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>
/// Copy of <c>nodes.py</c> <c>Node</c>: a class, because it defines <c>__eq__</c> (R7), with
/// <c>validate_assignment=True</c> on its validated field <c>labels</c>.
/// </summary>
internal abstract class Node : IPydanticModel
{
    private List<string> labels = [];

    internal string Uuid { get; set; } = Py.Uuid4();

    internal required string Name { get; set; }

    internal required string GroupId { get; set; }

    internal List<string> Labels
    {
        get => labels;
        set
        {
            Pydantic.Wrap<NodeLabelValidationException>(() => TestHelpers.ValidateNodeLabels(value), PythonName, "labels");
            labels = value;
        }
    }

    internal PyDateTime CreatedAt { get; set; } = PyDateTime.UtcNow(TimeProvider.System);

    internal abstract string PythonName { get; }

    public abstract OrderedDictionary<string, object?> ModelDump(bool json = false);
}

/// <summary>Copy of <c>nodes.py</c> <c>EntityNode</c>.</summary>
internal sealed class EntityNode : Node
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["uuid"] = new(false),
        ["name"] = new(true),
        ["group_id"] = new(true),
        ["labels"] = new(false),
        ["created_at"] = new(false),
        ["name_embedding"] = new(false),
        ["summary"] = new(false),
        ["attributes"] = new(false),
    };

    internal List<double>? NameEmbedding { get; set; }

    internal string Summary { get; set; } = string.Empty;

    internal OrderedDictionary<string, object?> Attributes { get; set; } = [];

    internal override string PythonName => "EntityNode";

    internal EntityNode Validate()
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => TestHelpers.ValidateNodeLabels(Labels), PythonName, "labels");
        return this;
    }

    internal static EntityNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EntityNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, () => PyDateTime.UtcNow(TimeProvider.System));
        var nameEmbedding = f.Default("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)), () => null);
        var summary = f.Default("summary", Pydantic.Str, () => string.Empty);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => []);
        f.ThrowIfAny();
        return new EntityNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            NameEmbedding = nameEmbedding.Value,
            Summary = summary.Value,
            Attributes = attributes.Value,
        }.Validate();
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("name_embedding", NameEmbedding),
        ("summary", Summary),
        ("attributes", Attributes));
}

/// <summary>Copy of <c>nodes.py</c> <c>EpisodicNode</c>.</summary>
internal sealed class EpisodicNode : Node
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["uuid"] = new(false),
        ["name"] = new(true),
        ["group_id"] = new(true),
        ["labels"] = new(false),
        ["created_at"] = new(false),
        ["source"] = new(true),
        ["source_description"] = new(true),
        ["content"] = new(true),
        ["valid_at"] = new(true),
        ["entity_edges"] = new(false),
        ["episode_metadata"] = new(false),
    };

    internal required EpisodeType Source { get; set; }

    internal required string SourceDescription { get; set; }

    internal required string Content { get; set; }

    internal required PyDateTime ValidAt { get; set; }

    internal List<string> EntityEdges { get; set; } = [];

    internal OrderedDictionary<string, object?>? EpisodeMetadata { get; set; }

    internal override string PythonName => "EpisodicNode";

    internal EpisodicNode Validate()
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => TestHelpers.ValidateNodeLabels(Labels), PythonName, "labels");
        return this;
    }

    internal static EpisodicNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EpisodicNode", data);
        var uuid = f.Default("uuid", Pydantic.Str, Py.Uuid4);
        var name = f.Required("name", Pydantic.Str);
        var groupId = f.Required("group_id", Pydantic.Str);
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var createdAt = f.Default("created_at", Pydantic.DateTime, () => PyDateTime.UtcNow(TimeProvider.System));
        var source = f.Required("source", Pydantic.Enum<EpisodeType>);
        var sourceDescription = f.Required("source_description", Pydantic.Str);
        var content = f.Required("content", Pydantic.Str);
        var validAt = f.Required("valid_at", Pydantic.DateTime);
        var entityEdges = f.Default("entity_edges", v => Pydantic.List(v, Pydantic.Str), () => []);
        var episodeMetadata = f.Default("episode_metadata", v => Pydantic.Optional(v, x => Pydantic.Dict(x, Pydantic.Any)), () => null);
        f.ThrowIfAny();
        return new EpisodicNode
        {
            Uuid = uuid.Value,
            Name = name.Value,
            GroupId = groupId.Value,
            Labels = labels.Value,
            CreatedAt = createdAt.Value,
            Source = source.Value,
            SourceDescription = sourceDescription.Value,
            Content = content.Value,
            ValidAt = validAt.Value,
            EntityEdges = entityEdges.Value,
            EpisodeMetadata = episodeMetadata.Value,
        }.Validate();
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("uuid", Uuid),
        ("name", Name),
        ("group_id", GroupId),
        ("labels", Labels),
        ("created_at", CreatedAt),
        ("source", Source),
        ("source_description", SourceDescription),
        ("content", Content),
        ("valid_at", ValidAt),
        ("entity_edges", EntityEdges),
        ("episode_metadata", EpisodeMetadata));
}

/// <summary>Copy of <c>prompts/dedupe_nodes.py</c> <c>NodeDuplicate</c>.</summary>
internal sealed record NodeDuplicate : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["id"] = new(true),
        ["name"] = new(true),
        ["duplicate_candidate_id"] = new(true),
    };

    internal required long Id { get; set; }

    internal required string Name { get; set; }

    internal required long DuplicateCandidateId { get; set; }

    internal NodeDuplicate Validate() => this;

    internal static NodeDuplicate ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeDuplicate", data);
        var id = f.Required("id", Pydantic.Int);
        var name = f.Required("name", Pydantic.Str);
        var duplicateCandidateId = f.Required("duplicate_candidate_id", Pydantic.Int);
        f.ThrowIfAny();
        return new NodeDuplicate { Id = id.Value, Name = name.Value, DuplicateCandidateId = duplicateCandidateId.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("id", Id), ("name", Name), ("duplicate_candidate_id", DuplicateCandidateId));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/dedupe_nodes.py</c> <c>NodeResolutions</c>.</summary>
internal sealed record NodeResolutions : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["entity_resolutions"] = new(true) };

    internal required List<NodeDuplicate> EntityResolutions { get; set; }

    internal NodeResolutions Validate() => this;

    internal static NodeResolutions ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeResolutions", data);
        var entityResolutions = f.Required("entity_resolutions", v => Pydantic.List(v, x => Pydantic.Model(x, "NodeDuplicate", NodeDuplicate.ModelValidate)));
        f.ThrowIfAny();
        return new NodeResolutions { EntityResolutions = entityResolutions.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("entity_resolutions", EntityResolutions));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/extract_edges.py</c> <c>Edge</c>.</summary>
internal sealed record Edge : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["source_entity_name"] = new(true),
        ["target_entity_name"] = new(true),
        ["relation_type"] = new(true),
        ["fact"] = new(true),
        ["valid_at"] = new(false),
        ["invalid_at"] = new(false),
        ["episode_indices"] = new(false),
    };

    internal required string SourceEntityName { get; set; }

    internal required string TargetEntityName { get; set; }

    internal required string RelationType { get; set; }

    internal required string Fact { get; set; }

    internal string? ValidAt { get; set; }

    internal string? InvalidAt { get; set; }

    internal List<long> EpisodeIndices { get; set; } = [0];

    internal Edge Validate() => this;

    internal static Edge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Edge", data);
        var sourceEntityName = f.Required("source_entity_name", Pydantic.Str);
        var targetEntityName = f.Required("target_entity_name", Pydantic.Str);
        var relationType = f.Required("relation_type", Pydantic.Str);
        var fact = f.Required("fact", Pydantic.Str);
        var validAt = f.Default("valid_at", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var invalidAt = f.Default("invalid_at", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var episodeIndices = f.Default("episode_indices", v => Pydantic.List(v, Pydantic.Int), () => [0]);
        f.ThrowIfAny();
        return new Edge
        {
            SourceEntityName = sourceEntityName.Value,
            TargetEntityName = targetEntityName.Value,
            RelationType = relationType.Value,
            Fact = fact.Value,
            ValidAt = validAt.Value,
            InvalidAt = invalidAt.Value,
            EpisodeIndices = episodeIndices.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("source_entity_name", SourceEntityName),
        ("target_entity_name", TargetEntityName),
        ("relation_type", RelationType),
        ("fact", Fact),
        ("valid_at", ValidAt),
        ("invalid_at", InvalidAt),
        ("episode_indices", EpisodeIndices));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/extract_edges.py</c> <c>ExtractedEdges</c>.</summary>
internal sealed record ExtractedEdges : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["edges"] = new(true) };

    internal required List<Edge> Edges { get; set; }

    internal ExtractedEdges Validate() => this;

    internal static ExtractedEdges ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ExtractedEdges", data);
        var edges = f.Required("edges", v => Pydantic.List(v, x => Pydantic.Model(x, "Edge", Edge.ModelValidate)));
        f.ThrowIfAny();
        return new ExtractedEdges { Edges = edges.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("edges", Edges));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/models.py</c> <c>SystemMessage</c>, with a <c>Literal</c> field.</summary>
internal sealed record SystemMessage : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["role"] = new(false), ["content"] = new(true) };

    internal string Role { get; set; } = "system";

    internal required string Content { get; set; }

    internal SystemMessage Validate() => this;

    internal static SystemMessage ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SystemMessage", data);
        var role = f.Default("role", v => Pydantic.Literal(v, "system"), () => "system");
        var content = f.Required("content", Pydantic.Str);
        f.ThrowIfAny();
        return new SystemMessage { Role = role.Value, Content = content.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("role", Role), ("content", Content));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/models.py</c> <c>UserMessage</c>.</summary>
internal sealed record UserMessage : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["role"] = new(false), ["content"] = new(true) };

    internal string Role { get; set; } = "user";

    internal required string Content { get; set; }

    internal UserMessage Validate() => this;

    internal static UserMessage ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("UserMessage", data);
        var role = f.Default("role", v => Pydantic.Literal(v, "user"), () => "user");
        var content = f.Required("content", Pydantic.Str);
        f.ThrowIfAny();
        return new UserMessage { Role = role.Value, Content = content.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("role", Role), ("content", Content));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>prompts/models.py</c> <c>ChatPrompt</c>, with two nested models.</summary>
internal sealed record ChatPrompt : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["system"] = new(true), ["user"] = new(true) };

    internal required SystemMessage System { get; set; }

    internal required UserMessage User { get; set; }

    internal ChatPrompt Validate() => this;

    internal static ChatPrompt ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ChatPrompt", data);
        var system = f.Required("system", v => Pydantic.Model(v, "SystemMessage", SystemMessage.ModelValidate));
        var user = f.Required("user", v => Pydantic.Model(v, "UserMessage", UserMessage.ModelValidate));
        f.ThrowIfAny();
        return new ChatPrompt { System = system.Value, User = user.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("system", System), ("user", User));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>search/search_config.py</c> <c>EdgeSearchConfig</c>, with enum fields and defaults.</summary>
internal sealed record EdgeSearchConfig : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["search_methods"] = new(true),
        ["reranker"] = new(false),
        ["sim_min_score"] = new(false),
        ["mmr_lambda"] = new(false),
        ["bfs_max_depth"] = new(false),
    };

    internal required List<EdgeSearchMethod> SearchMethods { get; set; }

    internal EdgeReranker Reranker { get; set; } = EdgeReranker.Rrf;

    internal double SimMinScore { get; set; } = 0.6;

    internal double MmrLambda { get; set; } = 0.5;

    internal long BfsMaxDepth { get; set; } = 3;

    internal EdgeSearchConfig Validate() => this;

    internal static EdgeSearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EdgeSearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<EdgeSearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<EdgeReranker>, () => EdgeReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => 0.6);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => 0.5);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => 3L);
        f.ThrowIfAny();
        return new EdgeSearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json, ("search_methods", SearchMethods), ("reranker", Reranker), ("sim_min_score", SimMinScore), ("mmr_lambda", MmrLambda), ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>search/search_config.py</c> <c>NodeSearchConfig</c>.</summary>
internal sealed record NodeSearchConfig : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["search_methods"] = new(true),
        ["reranker"] = new(false),
        ["sim_min_score"] = new(false),
        ["mmr_lambda"] = new(false),
        ["bfs_max_depth"] = new(false),
    };

    internal required List<NodeSearchMethod> SearchMethods { get; set; }

    internal NodeReranker Reranker { get; set; } = NodeReranker.Rrf;

    internal double SimMinScore { get; set; } = 0.6;

    internal double MmrLambda { get; set; } = 0.5;

    internal long BfsMaxDepth { get; set; } = 3;

    internal NodeSearchConfig Validate() => this;

    internal static NodeSearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeSearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<NodeSearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<NodeReranker>, () => NodeReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => 0.6);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => 0.5);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => 3L);
        f.ThrowIfAny();
        return new NodeSearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json, ("search_methods", SearchMethods), ("reranker", Reranker), ("sim_min_score", SimMinScore), ("mmr_lambda", MmrLambda), ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>search/search_config.py</c> <c>SearchConfig</c> without the episode and community configs, with optional nested models.</summary>
internal sealed record SearchConfig : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["edge_config"] = new(false),
        ["node_config"] = new(false),
        ["episode_config"] = new(false),
        ["community_config"] = new(false),
        ["limit"] = new(false),
        ["reranker_min_score"] = new(false),
    };

    internal EdgeSearchConfig? EdgeConfig { get; set; }

    internal NodeSearchConfig? NodeConfig { get; set; }

    internal long Limit { get; set; } = 10;

    internal double RerankerMinScore { get; set; }

    internal SearchConfig Validate() => this;

    internal static SearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SearchConfig", data);
        var edgeConfig = f.Default("edge_config", v => Pydantic.Optional(v, x => Pydantic.Model(x, "EdgeSearchConfig", EdgeSearchConfig.ModelValidate)), () => null);
        var nodeConfig = f.Default("node_config", v => Pydantic.Optional(v, x => Pydantic.Model(x, "NodeSearchConfig", NodeSearchConfig.ModelValidate)), () => null);
        var limit = f.Default("limit", Pydantic.Int, () => 10L);
        var rerankerMinScore = f.Default("reranker_min_score", Pydantic.Float, () => 0);
        f.ThrowIfAny();
        return new SearchConfig
        {
            EdgeConfig = edgeConfig.Value,
            NodeConfig = nodeConfig.Value,
            Limit = limit.Value,
            RerankerMinScore = rerankerMinScore.Value,
        }.Validate();
    }

    // PORT: the test copy has no episode or community config. Python dumps None for them.
    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json, ("edge_config", EdgeConfig), ("node_config", NodeConfig), ("episode_config", null), ("community_config", null), ("limit", Limit), ("reranker_min_score", RerankerMinScore));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Copy of <c>ingestion/config.py</c> <c>ConcurrentIngestionConfig</c>, with constraints and a model validator.</summary>
internal sealed record ConcurrentIngestionConfig : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new()
    {
        ["window"] = new(true),
        ["state_store"] = new(false),
        ["lease_seconds"] = new(false),
        ["write_margin_seconds"] = new(false),
        ["poll_interval_seconds"] = new(false),
        ["index_lag_seconds"] = new(false),
        ["max_apply_attempts"] = new(false),
        ["enabled_for_group"] = new(false),
        ["before_commit_write"] = new(false),
        ["before_enrichment_write"] = new(false),
    };

    internal required long Window { get; set; }

    internal object? StateStore { get; set; }

    internal double LeaseSeconds { get; set; } = 60.0;

    internal double WriteMarginSeconds { get; set; } = 5.0;

    internal double PollIntervalSeconds { get; set; } = 0.25;

    internal double IndexLagSeconds { get; set; } = 300.0;

    internal long MaxApplyAttempts { get; set; } = 5;

    internal Func<string, bool>? EnabledForGroup { get; set; }

    internal Func<object?, Task<object?>>? BeforeCommitWrite { get; set; }

    internal Func<List<object?>, object?, Task<List<object?>>>? BeforeEnrichmentWrite { get; set; }

    internal ConcurrentIngestionConfig Validate()
    {
        var f = new PydanticFields("ConcurrentIngestionConfig");
        f.Ge("window", Window, 1);
        f.Gt("lease_seconds", LeaseSeconds, 0);
        f.Ge("write_margin_seconds", WriteMarginSeconds, 0);
        f.Gt("poll_interval_seconds", PollIntervalSeconds, 0);
        f.Ge("index_lag_seconds", IndexLagSeconds, 0);
        f.Ge("max_apply_attempts", MaxApplyAttempts, 1);
        f.ThrowIfAny();
        Pydantic.Wrap(
            () =>
            {
                if (WriteMarginSeconds >= LeaseSeconds / 3)
                {
                    throw new ArgumentException("write_margin_seconds must be less than lease_seconds / 3");
                }

                if (StateStore is not null && StateStore is not IIngestionStateStore)
                {
                    throw new ArgumentException("state_store must be an IngestionStateStore");
                }
            },
            "ConcurrentIngestionConfig");
        return this;
    }

    internal static ConcurrentIngestionConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ConcurrentIngestionConfig", data);
        var window = f.Required("window", Pydantic.Int);
        var stateStore = f.Default("state_store", Pydantic.Any, () => null);
        var leaseSeconds = f.Default("lease_seconds", Pydantic.Float, () => 60.0);
        var writeMarginSeconds = f.Default("write_margin_seconds", Pydantic.Float, () => 5.0);
        var pollIntervalSeconds = f.Default("poll_interval_seconds", Pydantic.Float, () => 0.25);
        var indexLagSeconds = f.Default("index_lag_seconds", Pydantic.Float, () => 300.0);
        var maxApplyAttempts = f.Default("max_apply_attempts", Pydantic.Int, () => 5L);
        var enabledForGroup = f.Default("enabled_for_group", v => Pydantic.Optional(v, Pydantic.Callable<Func<string, bool>>), () => null);
        var beforeCommitWrite = f.Default("before_commit_write", v => Pydantic.Optional(v, Pydantic.Callable<Func<object?, Task<object?>>>), () => null);
        var beforeEnrichmentWrite = f.Default(
            "before_enrichment_write", v => Pydantic.Optional(v, Pydantic.Callable<Func<List<object?>, object?, Task<List<object?>>>>), () => null);
        f.ThrowIfAny();
        return new ConcurrentIngestionConfig
        {
            Window = window.Value,
            StateStore = stateStore.Value,
            LeaseSeconds = leaseSeconds.Value,
            WriteMarginSeconds = writeMarginSeconds.Value,
            PollIntervalSeconds = pollIntervalSeconds.Value,
            IndexLagSeconds = indexLagSeconds.Value,
            MaxApplyAttempts = maxApplyAttempts.Value,
            EnabledForGroup = enabledForGroup.Value,
            BeforeCommitWrite = beforeCommitWrite.Value,
            BeforeEnrichmentWrite = beforeEnrichmentWrite.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("window", Window),
        ("state_store", StateStore),
        ("lease_seconds", LeaseSeconds),
        ("write_margin_seconds", WriteMarginSeconds),
        ("poll_interval_seconds", PollIntervalSeconds),
        ("index_lag_seconds", IndexLagSeconds),
        ("max_apply_attempts", MaxApplyAttempts),
        ("enabled_for_group", EnabledForGroup),
        ("before_commit_write", BeforeCommitWrite),
        ("before_enrichment_write", BeforeEnrichmentWrite));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>The discriminated union <c>Operation</c> of <c>ingestion/change_set.py</c> (R7: an abstract record).</summary>
internal abstract record Operation : IPydanticModel
{
    internal static Operation ModelValidate(object? value) => Pydantic.Tagged<Operation>(
        value,
        "op",
        ("add_episodes_to_edge", AddEpisodesToEdge.ModelValidate),
        ("end_edge", EndEdge.ModelValidate),
        ("set_edge_attributes", SetEdgeAttributes.ModelValidate),
        ("patch_node", PatchNode.ModelValidate));

    public abstract OrderedDictionary<string, object?> ModelDump(bool json = false);
}

/// <summary>Copy of <c>ingestion/change_set.py</c> <c>AddEpisodesToEdge</c>.</summary>
internal sealed record AddEpisodesToEdge : Operation
{
    internal string Op { get; set; } = "add_episodes_to_edge";

    internal required string EdgeUuid { get; set; }

    internal required List<string> EpisodeUuids { get; set; }

    internal static AddEpisodesToEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("AddEpisodesToEdge", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "add_episodes_to_edge"), () => "add_episodes_to_edge");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var episodeUuids = f.Required("episode_uuids", v => Pydantic.List(v, Pydantic.Str));
        f.ThrowIfAny();
        return new AddEpisodesToEdge { Op = op.Value, EdgeUuid = edgeUuid.Value, EpisodeUuids = episodeUuids.Value };
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("op", Op), ("edge_uuid", EdgeUuid), ("episode_uuids", EpisodeUuids));
}

/// <summary>Copy of <c>ingestion/change_set.py</c> <c>EndEdge</c>.</summary>
internal sealed record EndEdge : Operation
{
    internal string Op { get; set; } = "end_edge";

    internal required string EdgeUuid { get; set; }

    internal PyDateTime? InvalidAt { get; set; }

    internal required PyDateTime ExpiredAt { get; set; }

    internal bool OnlyIfOpen { get; set; }

    internal static EndEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EndEdge", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "end_edge"), () => "end_edge");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var invalidAt = f.Default("invalid_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var expiredAt = f.Required("expired_at", Pydantic.DateTime);
        var onlyIfOpen = f.Default("only_if_open", Pydantic.Bool, () => false);
        f.ThrowIfAny();
        return new EndEdge { Op = op.Value, EdgeUuid = edgeUuid.Value, InvalidAt = invalidAt.Value, ExpiredAt = expiredAt.Value, OnlyIfOpen = onlyIfOpen.Value };
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json, ("op", Op), ("edge_uuid", EdgeUuid), ("invalid_at", InvalidAt), ("expired_at", ExpiredAt), ("only_if_open", OnlyIfOpen));
}

/// <summary>Copy of <c>ingestion/change_set.py</c> <c>SetEdgeAttributes</c>.</summary>
internal sealed record SetEdgeAttributes : Operation
{
    internal string Op { get; set; } = "set_edge_attributes";

    internal required string EdgeUuid { get; set; }

    internal OrderedDictionary<string, object?> BaseAttributes { get; set; } = [];

    internal OrderedDictionary<string, object?> Attributes { get; set; } = [];

    internal static SetEdgeAttributes ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SetEdgeAttributes", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "set_edge_attributes"), () => "set_edge_attributes");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var baseAttributes = f.Default("base_attributes", v => Pydantic.Dict(v, Pydantic.Any), () => []);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => []);
        f.ThrowIfAny();
        return new SetEdgeAttributes { Op = op.Value, EdgeUuid = edgeUuid.Value, BaseAttributes = baseAttributes.Value, Attributes = attributes.Value };
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("op", Op), ("edge_uuid", EdgeUuid), ("base_attributes", BaseAttributes), ("attributes", Attributes));
}

/// <summary>Copy of <c>ingestion/change_set.py</c> <c>PatchNode</c>.</summary>
internal sealed record PatchNode : Operation
{
    internal string Op { get; set; } = "patch_node";

    internal required string NodeUuid { get; set; }

    internal List<string> Labels { get; set; } = [];

    internal OrderedDictionary<string, object?> Attributes { get; set; } = [];

    internal static PatchNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("PatchNode", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "patch_node"), () => "patch_node");
        var nodeUuid = f.Required("node_uuid", Pydantic.Str);
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => []);
        f.ThrowIfAny();
        return new PatchNode { Op = op.Value, NodeUuid = nodeUuid.Value, Labels = labels.Value, Attributes = attributes.Value };
    }

    public override OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("op", Op), ("node_uuid", NodeUuid), ("labels", Labels), ("attributes", Attributes));
}

/// <summary>A test model with the one field of <c>ChangeSet</c> that holds the discriminated union.</summary>
internal sealed record Ops : IPydanticModel
{
    internal static readonly OrderedDictionary<string, PydanticField> ModelFields = new() { ["operations"] = new(false) };

    internal List<Operation> Operations { get; set; } = [];

    internal Ops Validate() => this;

    internal static Ops ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Ops", data);
        var operations = f.Default("operations", v => Pydantic.List(v, Operation.ModelValidate), () => []);
        f.ThrowIfAny();
        return new Ops { Operations = operations.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(json, ("operations", Operations));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}
