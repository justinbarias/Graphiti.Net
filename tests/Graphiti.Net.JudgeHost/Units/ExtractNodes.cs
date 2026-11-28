namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/extract_nodes.py</c>.</summary>
internal static class ExtractNodesBindings
{
    private const string Module = "graphiti_core.prompts.extract_nodes";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":extract_message", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractMessage));
        table.Pure.Add(Module + ":extract_json", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractJson));
        table.Pure.Add(Module + ":extract_text", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractText));
        table.Pure.Add(Module + ":classify_nodes", call => call.Signature("context").InvokeAsync(ExtractNodes.ClassifyNodes));
        table.Pure.Add(Module + ":extract_attributes", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractAttributes));
        table.Pure.Add(Module + ":extract_summary", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractSummary));
        table.Pure.Add(Module + ":_entity_type_descriptions_section", call => call.Signature("context").InvokeAsync(ExtractNodes.EntityTypeDescriptionsSection));
        table.Pure.Add(Module + ":extract_summaries_batch", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractSummariesBatch));
        table.Pure.Add(Module + ":extract_entity_summaries_from_episodes", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractEntitySummariesFromEpisodes));

        // The cases pass the class ExtractedEntity as a custom type, and they read only its __doc__ and its identity
        // (R22). No case passes it as a response model. The class has no docstring.
        // PORT: the C# model has no ModelFields, because upstream reads none, so the name and is_required() of each
        // Python field are written here. The descriptor differs from the Python class in two ways: no field has a
        // description, where Python has one for each field; and episode_indices is list[int] | None = None, where Python
        // has list[int] with default_factory=lambda: [0], so no anyOf and no default in the schema, and [0] for a
        // missing key. So its ModelJsonSchema and ModelValidate are not Python's. No judge case reads the schema or the
        // validation of this descriptor.
        // The cases also pass the class ExtractedEntities, both as a response model (PromptSpec) and as a custom type
        // (validate_entity_types, validate_excluded_entity_types, validate_identity_properties).
        // PORT: the $ref is a GraphTypeDescriptor, which is also an IResponseModel, so it serves both uses. The descriptor
        // is written here and not built from the product model ExtractedEntities.ResponseModel (a BuiltinResponseModel),
        // which a custom type parameter cannot hold. It differs from the Python class in these ways: the field
        // extracted_entities is list[ExtractedEntity] in Python, and the descriptor has the placeholder kind String with
        // IsList, so a list of strings; the field has no description ("List of extracted entities" in Python); and the
        // descriptor has no docstring, as the Python class has none. So its ModelJsonSchema and its ModelValidate are not
        // Python's, and no judge case reads them (the schema case reads the table of schemas below). The three functions of
        // the ten cases (validate_entity_types, validate_excluded_entity_types, validate_identity_properties) read only the
        // field names, and the encoder of a response model reads only the name.
        table.Registry
            .AddRef(Module + ":extract_message", (PromptFunction)ExtractNodes.ExtractMessage)
            .AddRef(
                Module + ":ExtractedEntity",
                new GraphTypeDescriptor(
                    "ExtractedEntity",
                    null,
                    [
                        new GraphFieldDescriptor("name", GraphFieldKind.String, false, null, true, null),
                        new GraphFieldDescriptor("entity_type_id", GraphFieldKind.Integer, false, null, true, null),
                        new GraphFieldDescriptor("episode_indices", GraphFieldKind.Integer, true, null, false, null),
                    ]))
            .AddRef(
                Module + ":ExtractedEntities",
                new GraphTypeDescriptor(
                    "ExtractedEntities",
                    null,
                    [new GraphFieldDescriptor("extracted_entities", GraphFieldKind.String, true, null, true, null)]))
            .AddModel(Module + ":ExtractedEntity", data => ExtractedEntity.ModelValidate(data));

        table.Schemas.Add(Module + ":ExtractedEntity", ExtractedEntity.ModelJsonSchema);
        table.Schemas.Add(Module + ":ExtractedEntities", ExtractedEntities.ModelJsonSchema);
        table.Schemas.Add(Module + ":EntitySummary", EntitySummary.ModelJsonSchema);
        table.Schemas.Add(Module + ":SummarizedEntity", SummarizedEntity.ModelJsonSchema);
        table.Schemas.Add(Module + ":SummarizedEntities", SummarizedEntities.ModelJsonSchema);
    }
}
