namespace Graphiti.PyCompat.Tests.Models;

using Graphiti.PyCompat;

// Test-only descriptions of the 49 models of port/judge/cases/pure/schemas.jsonl with the PydanticSchema builder,
// in the order of their nesting. Each one is what a ported model writes as its ModelJsonSchema (RULES.md R7).

/// <summary>The schemas of the upstream response models, by the Python class name.</summary>
internal static class UpstreamSchemas
{
    internal static readonly PydanticSchema ExtractedEntity = new PydanticSchema("ExtractedEntity")
        .Required("name", PydanticType.Str, "Name of the extracted entity")
        .Required("entity_type_id", PydanticType.Int, "ID of the classified entity type. Must be one of the provided entity_type_id integers.")
        .Factory("episode_indices", PydanticType.List(PydanticType.Int), "List of episode numbers (0-indexed) this entity was extracted from. When processing a single episode, this should be [0].");

    internal static readonly PydanticSchema ExtractedEntities = new PydanticSchema("ExtractedEntities")
        .Required("extracted_entities", PydanticType.List(PydanticType.Model(ExtractedEntity)), "List of extracted entities");

    internal static readonly PydanticSchema EntitySummary = new PydanticSchema("EntitySummary")
        .Required("summary", PydanticType.Str, "Summary of the entity");

    internal static readonly PydanticSchema SummarizedEntity = new PydanticSchema("SummarizedEntity")
        .Required("name", PydanticType.Str, "Name of the entity being summarized")
        .Required("summary", PydanticType.Str, "Updated summary for the entity");

    internal static readonly PydanticSchema SummarizedEntities = new PydanticSchema("SummarizedEntities")
        .Required("summaries", PydanticType.List(PydanticType.Model(SummarizedEntity)), "List of entity summaries. Only include entities that need summary updates.");

    internal static readonly PydanticSchema Edge = new PydanticSchema("Edge")
        .Required("source_entity_name", PydanticType.Str, "The name of the source entity from the ENTITIES list")
        .Required("target_entity_name", PydanticType.Str, "The name of the target entity from the ENTITIES list")
        .Required("relation_type", PydanticType.Str, "The type of relationship between the entities, in SCREAMING_SNAKE_CASE (e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH)")
        .Required("fact", PydanticType.Str, "A natural language description of the relationship between the entities, paraphrased from the source text")
        .Default("valid_at", PydanticType.Optional(PydanticType.Str), null, "The date and time when the relationship or assertion described by the edge fact became true or was established. Future-tense plans/promises use the statement reference time, not the future occurrence date. Use ISO 8601 format (YYYY-MM-DDTHH:MM:SS.SSSSSSZ)")
        .Default("invalid_at", PydanticType.Optional(PydanticType.Str), null, "The date and time when the relationship described by the edge fact stopped being true or ended. Soft deadlines are not invalid_at values. Use ISO 8601 format (YYYY-MM-DDTHH:MM:SS.SSSSSSZ)")
        .Factory("episode_indices", PydanticType.List(PydanticType.Int), "List of episode numbers (0-indexed) that this fact was derived from. When processing a single episode, this should be [0].");

    internal static readonly PydanticSchema ExtractedEdges = new PydanticSchema("ExtractedEdges")
        .Required("edges", PydanticType.List(PydanticType.Model(Edge)));

    internal static readonly PydanticSchema EdgeTimestamps = new PydanticSchema("EdgeTimestamps", "Temporal bounds extracted from a fact.")
        .Default("valid_at", PydanticType.Optional(PydanticType.Str), null, "When the fact became true. Future-tense plans/promises use the reference time, not the future occurrence date. ISO 8601 with Z suffix (e.g., 2025-04-30T00:00:00Z)")
        .Default("invalid_at", PydanticType.Optional(PydanticType.Str), null, "When the fact stopped being true. Soft deadlines are not invalid_at values. ISO 8601 with Z suffix (e.g., 2025-04-30T00:00:00Z)");

    internal static readonly PydanticSchema BatchEdgeTimestamps = new PydanticSchema("BatchEdgeTimestamps", "Temporal bounds for a batch of facts.")
        .Required("timestamps", PydanticType.List(PydanticType.Model(EdgeTimestamps)), "Timestamps for each fact, in the same order as the input facts");

    internal static readonly PydanticSchema CombinedEntity = new PydanticSchema("CombinedEntity", "Entity extracted by the combined node+edge extraction prompt.")
        .Required("name", PydanticType.Str, "Name of the extracted entity")
        .Required("entity_type_id", PydanticType.Int, "ID of the classified entity type. Must be one of the provided entity_type_id integers.");

    internal static readonly PydanticSchema CombinedFact = new PydanticSchema("CombinedFact", "Relationship fact extracted by the combined node+edge extraction prompt.")
        .Required("source_entity_name", PydanticType.Str, "The name of the source entity. MUST exactly match an entity `name` in extracted_entities (including value-like endpoints such as versions).")
        .Required("target_entity_name", PydanticType.Str, "The name of the target entity. MUST exactly match an entity `name` in extracted_entities (including value-like endpoints such as versions).")
        .Required("relation_type", PydanticType.Str, "The type of relationship between the entities, in SCREAMING_SNAKE_CASE (e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH)")
        .Required("fact", PydanticType.Str, "A self-contained natural language description of the relationship, paraphrased from the source text with all specific details preserved")
        .Factory("episode_indices", PydanticType.List(PydanticType.Int), "List of episode numbers (0-indexed) that this fact was derived from. When processing a single episode, this should be [0].");

    internal static readonly PydanticSchema CombinedExtraction = new PydanticSchema("CombinedExtraction", "Combined node and edge extraction response.")
        .Required("extracted_entities", PydanticType.List(PydanticType.Model(CombinedEntity)), "List of extracted entities")
        .Required("edges", PydanticType.List(PydanticType.Model(CombinedFact)), "List of extracted relationship facts");

    internal static readonly PydanticSchema NodeDuplicate = new PydanticSchema("NodeDuplicate")
        .Required("id", PydanticType.Int, "integer id of the entity")
        .Required("name", PydanticType.Str, "Name of the entity. Should be the most complete and descriptive name of the entity. Do not include any JSON formatting in the Entity name such as {}.")
        .Required("duplicate_candidate_id", PydanticType.Int, "candidate_id of the matching EXISTING ENTITY, or -1 if no duplicate exists.");

    internal static readonly PydanticSchema NodeResolutions = new PydanticSchema("NodeResolutions")
        .Required("entity_resolutions", PydanticType.List(PydanticType.Model(NodeDuplicate)), "List of resolved nodes");

    internal static readonly PydanticSchema EdgeDuplicate = new PydanticSchema("EdgeDuplicate")
        .Required("duplicate_facts", PydanticType.List(PydanticType.Int), "List of idx values of duplicate facts (only from EXISTING FACTS range). Empty list if none.")
        .Required("contradicted_facts", PydanticType.List(PydanticType.Int), "List of idx values of contradicted facts (from full idx range). Empty list if none.");

    internal static readonly PydanticSchema Summary = new PydanticSchema("Summary")
        .Required("summary", PydanticType.Str, "Summary containing the important information about the entity. Under 1000 characters");

    internal static readonly PydanticSchema SummaryDescription = new PydanticSchema("SummaryDescription")
        .Required("description", PydanticType.Str, "One sentence description of the provided summary");

    internal static readonly PydanticSchema SagaSummary = new PydanticSchema("SagaSummary")
        .Required("summary", PydanticType.Str, "Factual knowledge brief. Under 1000 characters.");

    internal static readonly PydanticSchema QueryExpansion = new PydanticSchema("QueryExpansion")
        .Required("query", PydanticType.Str, "query optimized for database search");

    internal static readonly PydanticSchema QAResponse = new PydanticSchema("QAResponse")
        .Required("ANSWER", PydanticType.Str, "how Alice would answer the question");

    internal static readonly PydanticSchema EvalResponse = new PydanticSchema("EvalResponse")
        .Required("is_correct", PydanticType.Bool, "boolean if the answer is correct or incorrect")
        .Required("reasoning", PydanticType.Str, "why you determined the response was correct or incorrect");

    internal static readonly PydanticSchema EvalAddEpisodeResults = new PydanticSchema("EvalAddEpisodeResults")
        .Required("candidate_is_worse", PydanticType.Bool, "boolean if the baseline extraction is higher quality than the candidate extraction.")
        .Required("reasoning", PydanticType.Str, "why you determined the response was correct or incorrect");

    internal static readonly PydanticSchema Message = new PydanticSchema("Message", "LLM transport message (role + content). Used by LLMClient.generate_response.")
        .Required("role", PydanticType.Str)
        .Required("content", PydanticType.Str);

    internal static readonly PydanticSchema SystemMessage = new PydanticSchema("SystemMessage")
        .Default("role", PydanticType.Literal("system"), "system")
        .Required("content", PydanticType.Str);

    internal static readonly PydanticSchema UserMessage = new PydanticSchema("UserMessage")
        .Default("role", PydanticType.Literal("user"), "user")
        .Required("content", PydanticType.Str);

    internal static readonly PydanticSchema ChatPrompt = new PydanticSchema("ChatPrompt", "Typed system+user prompt returned by all prompt builders.")
        .Required("system", PydanticType.Model(SystemMessage))
        .Required("user", PydanticType.Model(UserMessage));

    internal static readonly PydanticSchema Neighbor = new PydanticSchema("Neighbor")
        .Required("node_uuid", PydanticType.Str)
        .Required("edge_count", PydanticType.Int);

    internal static readonly PydanticSchema EntityNode = new PydanticSchema("EntityNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("name_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the name")
        .Factory("summary", PydanticType.Str, "regional summary of surrounding edges")
        .Default("attributes", PydanticType.Dict(PydanticType.Any), new OrderedDictionary<string, object?>(), "Additional attributes of the node. Dependent on node labels");

    internal static readonly PydanticSchema EpisodicNode = new PydanticSchema("EpisodicNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Required("source", PydanticType.Enum<EpisodeType>("Enumeration of different types of episodes that can be processed.\n\nThis enum defines the various sources or formats of episodes that the system\ncan handle. It's used to categorize and potentially handle different types\nof input data differently.\n\nAttributes:\n-----------\nmessage : str\n    Represents a standard message-type episode. The content for this type\n    should be formatted as \"actor: content\". For example, \"user: Hello, how are you?\"\n    or \"assistant: I'm doing well, thank you for asking.\"\njson : str\n    Represents an episode containing a JSON string object with structured data.\ntext : str\n    Represents a plain text episode."), "source type")
        .Required("source_description", PydanticType.Str, "description of the data source")
        .Required("content", PydanticType.Str, "raw episode data")
        .Required("valid_at", PydanticType.DateTime, "datetime of when the original document was created")
        .Factory("entity_edges", PydanticType.List(PydanticType.Str), "list of entity edges referenced in this episode")
        .Default("episode_metadata", PydanticType.Optional(PydanticType.Dict(PydanticType.Any)), null, "customer-defined metadata key-value pairs for filtering");

    internal static readonly PydanticSchema CommunityNode = new PydanticSchema("CommunityNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("name_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the name")
        .Factory("summary", PydanticType.Str, "region summary of member nodes");

    internal static readonly PydanticSchema SagaNode = new PydanticSchema("SagaNode")
        .Factory("uuid", PydanticType.Str)
        .Required("name", PydanticType.Str, "name of the node")
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Factory("labels", PydanticType.List(PydanticType.Str))
        .Factory("created_at", PydanticType.DateTime)
        .Default("summary", PydanticType.Str, "")
        .Default("first_episode_uuid", PydanticType.Optional(PydanticType.Str), null)
        .Default("last_episode_uuid", PydanticType.Optional(PydanticType.Str), null)
        .Default("last_summarized_at", PydanticType.Optional(PydanticType.DateTime), null)
        .Default("last_summarized_episode_valid_at", PydanticType.Optional(PydanticType.DateTime), null);

    internal static readonly PydanticSchema EntityEdge = new PydanticSchema("EntityEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime)
        .Required("name", PydanticType.Str, "name of the edge, relation name")
        .Required("fact", PydanticType.Str, "fact representing the edge and nodes that it connects")
        .Default("fact_embedding", PydanticType.Optional(PydanticType.List(PydanticType.Float)), null, "embedding of the fact")
        .Default("episodes", PydanticType.List(PydanticType.Str), new List<object?>(), "list of episode ids that reference these entity edges")
        .Default("expired_at", PydanticType.Optional(PydanticType.DateTime), null, "datetime of when Graphiti marked the edge as invalidated (transaction time); use invalid_at to find when the fact stopped being true")
        .Default("valid_at", PydanticType.Optional(PydanticType.DateTime), null, "datetime of when the fact became true; future-tense plans/promises use the episode reference time rather than the future occurrence date")
        .Default("invalid_at", PydanticType.Optional(PydanticType.DateTime), null, "datetime of when the fact stopped being true; soft deadlines are not invalid_at")
        .Default("reference_time", PydanticType.Optional(PydanticType.DateTime), null, "reference timestamp from the episode that produced this edge")
        .Default("attributes", PydanticType.Dict(PydanticType.Any), new OrderedDictionary<string, object?>(), "Additional attributes of the edge. Dependent on edge name");

    internal static readonly PydanticSchema EpisodicEdge = new PydanticSchema("EpisodicEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    internal static readonly PydanticSchema CommunityEdge = new PydanticSchema("CommunityEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    internal static readonly PydanticSchema HasEpisodeEdge = new PydanticSchema("HasEpisodeEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    internal static readonly PydanticSchema NextEpisodeEdge = new PydanticSchema("NextEpisodeEdge")
        .Factory("uuid", PydanticType.Str)
        .Required("group_id", PydanticType.Str, "partition of the graph")
        .Required("source_node_uuid", PydanticType.Str)
        .Required("target_node_uuid", PydanticType.Str)
        .Required("created_at", PydanticType.DateTime);

    internal static readonly PydanticSchema EdgeSearchConfig = new PydanticSchema("EdgeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<EdgeSearchMethod>()))
        .Default("reranker", PydanticType.Enum<EdgeReranker>(), EdgeReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, 0.6)
        .Default("mmr_lambda", PydanticType.Float, 0.5)
        .Default("bfs_max_depth", PydanticType.Int, 3L);

    internal static readonly PydanticSchema NodeSearchConfig = new PydanticSchema("NodeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<NodeSearchMethod>()))
        .Default("reranker", PydanticType.Enum<NodeReranker>(), NodeReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, 0.6)
        .Default("mmr_lambda", PydanticType.Float, 0.5)
        .Default("bfs_max_depth", PydanticType.Int, 3L);

    internal static readonly PydanticSchema EpisodeSearchConfig = new PydanticSchema("EpisodeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum("EpisodeSearchMethod", ["bm25"])))
        .Default("reranker", PydanticType.Enum("EpisodeReranker", ["reciprocal_rank_fusion", "cross_encoder"]), "reciprocal_rank_fusion")
        .Default("sim_min_score", PydanticType.Float, 0.6)
        .Default("mmr_lambda", PydanticType.Float, 0.5)
        .Default("bfs_max_depth", PydanticType.Int, 3L);

    internal static readonly PydanticSchema CommunitySearchConfig = new PydanticSchema("CommunitySearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum("CommunitySearchMethod", ["cosine_similarity", "bm25"])))
        .Default("reranker", PydanticType.Enum("CommunityReranker", ["reciprocal_rank_fusion", "mmr", "cross_encoder"]), "reciprocal_rank_fusion")
        .Default("sim_min_score", PydanticType.Float, 0.6)
        .Default("mmr_lambda", PydanticType.Float, 0.5)
        .Default("bfs_max_depth", PydanticType.Int, 3L);

    internal static readonly PydanticSchema SearchConfig = new PydanticSchema("SearchConfig")
        .Default("edge_config", PydanticType.Optional(PydanticType.Model(EdgeSearchConfig)), null)
        .Default("node_config", PydanticType.Optional(PydanticType.Model(NodeSearchConfig)), null)
        .Default("episode_config", PydanticType.Optional(PydanticType.Model(EpisodeSearchConfig)), null)
        .Default("community_config", PydanticType.Optional(PydanticType.Model(CommunitySearchConfig)), null)
        .Default("limit", PydanticType.Int, 10L)
        .Default("reranker_min_score", PydanticType.Float, 0L);

    internal static readonly PydanticSchema SearchResults = new PydanticSchema("SearchResults")
        .Factory("edges", PydanticType.List(PydanticType.Model(EntityEdge)))
        .Factory("edge_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("nodes", PydanticType.List(PydanticType.Model(EntityNode)))
        .Factory("node_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("episodes", PydanticType.List(PydanticType.Model(EpisodicNode)))
        .Factory("episode_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("communities", PydanticType.List(PydanticType.Model(CommunityNode)))
        .Factory("community_reranker_scores", PydanticType.List(PydanticType.Float));

    internal static readonly PydanticSchema DateFilter = new PydanticSchema("DateFilter")
        .Default("date", PydanticType.Optional(PydanticType.DateTime), null, "A datetime to filter on")
        .Required("comparison_operator", PydanticType.Enum<ComparisonOperator>(), "Comparison operator for date filter");

    internal static readonly PydanticSchema PropertyFilter = new PydanticSchema("PropertyFilter")
        .Required("property_name", PydanticType.Str, "Property name")
        .Default("property_value", PydanticType.Union(PydanticType.Str, PydanticType.Int, PydanticType.Float, PydanticType.Null), null, "Value you want to match on for the property")
        .Required("comparison_operator", PydanticType.Enum<ComparisonOperator>(), "Comparison operator for the property");

    internal static readonly PydanticSchema SearchFilters = new PydanticSchema("SearchFilters")
        .Default("node_labels", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "List of node labels to filter on")
        .Default("edge_types", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "List of edge types to filter on")
        .Default("valid_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter)))), null)
        .Default("invalid_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter)))), null)
        .Default("created_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter)))), null)
        .Default("expired_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter)))), null)
        .Default("edge_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null)
        .Default("edge_source_node_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "Restrict edge results to these source node UUIDs")
        .Default("edge_target_node_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "Restrict edge results to these target node UUIDs")
        .Default("property_filters", PydanticType.Optional(PydanticType.List(PydanticType.Model(PropertyFilter))), null);

    internal static readonly PydanticSchema AddEpisodeResults = new PydanticSchema("AddEpisodeResults")
        .Required("episode", PydanticType.Model(EpisodicNode))
        .Required("episodic_edges", PydanticType.List(PydanticType.Model(EpisodicEdge)))
        .Required("nodes", PydanticType.List(PydanticType.Model(EntityNode)))
        .Required("edges", PydanticType.List(PydanticType.Model(EntityEdge)))
        .Required("communities", PydanticType.List(PydanticType.Model(CommunityNode)))
        .Required("community_edges", PydanticType.List(PydanticType.Model(CommunityEdge)));

    internal static readonly PydanticSchema AddBulkEpisodeResults = new PydanticSchema("AddBulkEpisodeResults")
        .Required("episodes", PydanticType.List(PydanticType.Model(EpisodicNode)))
        .Required("episodic_edges", PydanticType.List(PydanticType.Model(EpisodicEdge)))
        .Required("nodes", PydanticType.List(PydanticType.Model(EntityNode)))
        .Required("edges", PydanticType.List(PydanticType.Model(EntityEdge)))
        .Required("communities", PydanticType.List(PydanticType.Model(CommunityNode)))
        .Required("community_edges", PydanticType.List(PydanticType.Model(CommunityEdge)));

    internal static readonly PydanticSchema AddTripletResults = new PydanticSchema("AddTripletResults")
        .Required("nodes", PydanticType.List(PydanticType.Model(EntityNode)))
        .Required("edges", PydanticType.List(PydanticType.Model(EntityEdge)));

    internal static readonly PydanticSchema RawEpisode = new PydanticSchema("RawEpisode")
        .Required("name", PydanticType.Str)
        .Default("uuid", PydanticType.Optional(PydanticType.Str), null)
        .Required("content", PydanticType.Str)
        .Required("source_description", PydanticType.Str)
        .Required("source", PydanticType.Enum<EpisodeType>("Enumeration of different types of episodes that can be processed.\n\nThis enum defines the various sources or formats of episodes that the system\ncan handle. It's used to categorize and potentially handle different types\nof input data differently.\n\nAttributes:\n-----------\nmessage : str\n    Represents a standard message-type episode. The content for this type\n    should be formatted as \"actor: content\". For example, \"user: Hello, how are you?\"\n    or \"assistant: I'm doing well, thank you for asking.\"\njson : str\n    Represents an episode containing a JSON string object with structured data.\ntext : str\n    Represents a plain text episode."))
        .Required("reference_time", PydanticType.DateTime);
    internal static readonly Dictionary<string, PydanticSchema> ByName = new()
    {
        ["ExtractedEntity"] = ExtractedEntity,
        ["ExtractedEntities"] = ExtractedEntities,
        ["EntitySummary"] = EntitySummary,
        ["SummarizedEntity"] = SummarizedEntity,
        ["SummarizedEntities"] = SummarizedEntities,
        ["Edge"] = Edge,
        ["ExtractedEdges"] = ExtractedEdges,
        ["EdgeTimestamps"] = EdgeTimestamps,
        ["BatchEdgeTimestamps"] = BatchEdgeTimestamps,
        ["CombinedEntity"] = CombinedEntity,
        ["CombinedFact"] = CombinedFact,
        ["CombinedExtraction"] = CombinedExtraction,
        ["NodeDuplicate"] = NodeDuplicate,
        ["NodeResolutions"] = NodeResolutions,
        ["EdgeDuplicate"] = EdgeDuplicate,
        ["Summary"] = Summary,
        ["SummaryDescription"] = SummaryDescription,
        ["SagaSummary"] = SagaSummary,
        ["QueryExpansion"] = QueryExpansion,
        ["QAResponse"] = QAResponse,
        ["EvalResponse"] = EvalResponse,
        ["EvalAddEpisodeResults"] = EvalAddEpisodeResults,
        ["Message"] = Message,
        ["SystemMessage"] = SystemMessage,
        ["UserMessage"] = UserMessage,
        ["ChatPrompt"] = ChatPrompt,
        ["Neighbor"] = Neighbor,
        ["EntityNode"] = EntityNode,
        ["EpisodicNode"] = EpisodicNode,
        ["CommunityNode"] = CommunityNode,
        ["SagaNode"] = SagaNode,
        ["EntityEdge"] = EntityEdge,
        ["EpisodicEdge"] = EpisodicEdge,
        ["CommunityEdge"] = CommunityEdge,
        ["HasEpisodeEdge"] = HasEpisodeEdge,
        ["NextEpisodeEdge"] = NextEpisodeEdge,
        ["EdgeSearchConfig"] = EdgeSearchConfig,
        ["NodeSearchConfig"] = NodeSearchConfig,
        ["EpisodeSearchConfig"] = EpisodeSearchConfig,
        ["CommunitySearchConfig"] = CommunitySearchConfig,
        ["SearchConfig"] = SearchConfig,
        ["SearchResults"] = SearchResults,
        ["DateFilter"] = DateFilter,
        ["PropertyFilter"] = PropertyFilter,
        ["SearchFilters"] = SearchFilters,
        ["AddEpisodeResults"] = AddEpisodeResults,
        ["AddBulkEpisodeResults"] = AddBulkEpisodeResults,
        ["AddTripletResults"] = AddTripletResults,
        ["RawEpisode"] = RawEpisode,
    };
}
