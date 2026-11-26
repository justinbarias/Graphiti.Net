namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.LlmClient;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/llm_client/decision_prompts.py</c>.</summary>
internal static class DecisionPromptsBindings
{
    private const string Module = "graphiti_core.llm_client.decision_prompts:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "tag", call => call.Signature("text", "name").InvokeAsync(DecisionPrompts.Tag));
        table.Pure.Add(Module + "parse_list", call => call.Signature("text").InvokeAsync(DecisionPrompts.ParseList));
        table.Pure.Add(Module + "shrink_state", call => call.Signature("state").InvokeAsync(DecisionPrompts.ShrinkState));
        table.Pure.Add(Module + "node_n6", call => call.Signature("user").InvokeAsync(DecisionPrompts.NodeN6));
        table.Pure.Add(Module + "_contra_strict", call => call.Signature("new_fact", "f").InvokeAsync(DecisionPrompts.ContraStrict));
        table.Pure.Add(Module + "edge_e2", call => call.Signature("user").InvokeAsync(DecisionPrompts.EdgeE2));
        table.Pure.Add(Module + "_user_text", call => call.Signature("messages").InvokeAsync(DecisionPrompts.UserText));
        table.Pure.Add(Module + "_encode_payload", call => call.Signature("state", "questions").InvokeAsync(DecisionPrompts.EncodePayload));
        table.Pure.Add(Module + "assemble_node_answers", call => call.Signature("answers", "entities").InvokeAsync(DecisionPrompts.AssembleNodeAnswers));
        table.Pure.Add(Module + "assemble_edge_answers", call => call.Signature("answers", "existing_indices", "contradiction_indices", "duplicate_threshold", "contradiction_threshold").InvokeAsync(DecisionPrompts.AssembleEdgeAnswers));

        // builtins:repr, which the cases call on each constant, is bound in Units/Builtins.cs.
        table.Registry.AddRef(Module + "RULES_HEADER", DecisionPrompts.RulesHeader);
        table.Registry.AddRef(Module + "NODE_RULES", DecisionPrompts.NodeRules);
        table.Registry.AddRef(Module + "NODE_NONE", DecisionPrompts.NodeNone);
        table.Registry.AddRef(Module + "DUP_RULES", DecisionPrompts.DupRules);
        table.Registry.AddRef(Module + "CON_RULES", DecisionPrompts.ConRules);
    }
}
