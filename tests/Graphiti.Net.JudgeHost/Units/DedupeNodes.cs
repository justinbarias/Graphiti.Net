namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/dedupe_nodes.py</c>.</summary>
internal static class DedupeNodesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:node", call => call.Signature("context").InvokeAsync(DedupeNodes.Node));
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:nodes", call => call.Signature("context").InvokeAsync(DedupeNodes.Nodes));
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:node_list", call => call.Signature("context").InvokeAsync(DedupeNodes.NodeList));

        table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeDuplicate", NodeDuplicate.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeResolutions", NodeResolutions.ModelJsonSchema);

        // The cases pass the classes as custom types, and they read only __doc__ and the identity of each class (R22).
        // Neither class has a docstring.
        // PORT: the C# models have no ModelFields, because upstream reads none, so the name and is_required() of each
        // Python field are written here. The descriptors differ from the Python classes: no field has a description,
        // where Python has one for each field of NodeDuplicate and for entity_resolutions; and entity_resolutions is
        // list[str], because the descriptor has no kind for list[NodeDuplicate], where Python has a list of models with
        // $defs. So their ModelJsonSchema and ModelValidate are not Python's. No judge case reads the schema or the
        // validation of these descriptors.
        table.Registry
            .AddRef(
                "graphiti_core.prompts.dedupe_nodes:NodeDuplicate",
                new GraphTypeDescriptor(
                    "NodeDuplicate",
                    null,
                    [
                        new GraphFieldDescriptor("id", GraphFieldKind.Integer, false, null, true, null),
                        new GraphFieldDescriptor("name", GraphFieldKind.String, false, null, true, null),
                        new GraphFieldDescriptor("duplicate_candidate_id", GraphFieldKind.Integer, false, null, true, null),
                    ]))
            .AddRef(
                "graphiti_core.prompts.dedupe_nodes:NodeResolutions",
                new GraphTypeDescriptor(
                    "NodeResolutions",
                    null,
                    [new GraphFieldDescriptor("entity_resolutions", GraphFieldKind.String, true, null, true, null)]));
    }
}
