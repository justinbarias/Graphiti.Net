namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/errors.py</c>. Each case builds one exception, and the result is its class name.</summary>
internal static class ErrorsBindings
{
    internal static void Register(BindingTable table)
    {
        // GraphitiError has no __init__ of its own: the Python call takes a message or none.
        table.Pure.Add("graphiti_core.errors:GraphitiError", call => GraphitiError(call.Signature("message")));
        table.Pure.Add("graphiti_core.errors:EdgeNotFoundError", call => Codec.Result(new EdgeNotFoundException(call.Signature("uuid").String("uuid"))));
        table.Pure.Add("graphiti_core.errors:EdgesNotFoundError", call => Codec.Result(new EdgesNotFoundException(call.Signature("uuids").List<string>("uuids"))));
        table.Pure.Add("graphiti_core.errors:GroupsEdgesNotFoundError", call => Codec.Result(new GroupsEdgesNotFoundException(call.Signature("group_ids").List<string>("group_ids"))));
        table.Pure.Add("graphiti_core.errors:GroupsNodesNotFoundError", call => Codec.Result(new GroupsNodesNotFoundException(call.Signature("group_ids").List<string>("group_ids"))));
        table.Pure.Add("graphiti_core.errors:NodeNotFoundError", call => Codec.Result(new NodeNotFoundException(call.Signature("uuid").String("uuid"))));
        table.Pure.Add("graphiti_core.errors:SearchRerankerError", call => Codec.Result(new SearchRerankerException(call.Signature("text").String("text"))));
        table.Pure.Add("graphiti_core.errors:EntityTypeValidationError", call =>
        {
            var p = call.Signature("entity_type", "entity_type_attribute");
            return Codec.Result(new EntityTypeValidationException(p.String("entity_type"), p.String("entity_type_attribute")));
        });
        table.Pure.Add("graphiti_core.errors:IdentityPropertyValidationError", call =>
        {
            var p = call.Signature("entity_type", "reason");
            return Codec.Result(new IdentityPropertyValidationException(p.String("entity_type"), p.String("reason")));
        });
        table.Pure.Add("graphiti_core.errors:GroupIdValidationError", call => Codec.Result(new GroupIdValidationException(call.Signature("group_id").String("group_id"))));
        table.Pure.Add("graphiti_core.errors:NodeLabelValidationError", call => Codec.Result(new NodeLabelValidationException(call.Signature("node_labels").List<string>("node_labels"))));
        table.Pure.Add("graphiti_core.errors:UnitExpiredError", call =>
        {
            var p = call.Signature("group_id", "seq");
            return Codec.Result(new UnitExpiredException(p.String("group_id"), p.Long("seq")));
        });
        table.Pure.Add("graphiti_core.errors:UnitInProgressError", call =>
        {
            var p = call.Signature("group_id", "unit_id", "seq");
            return Codec.Result(new UnitInProgressException(p.String("group_id"), p.String("unit_id"), p.Long("seq")));
        });
        table.Pure.Add("graphiti_core.errors:StaleCommitError", call =>
        {
            var p = call.Signature("group_id", "seq");
            return Codec.Result(new StaleCommitException(p.String("group_id"), p.Long("seq")));
        });
        table.Pure.Add("graphiti_core.errors:ChangeSetRejected", call => ChangeSetRejected(call.Signature("reason")));
    }

    // A case can leave out the argument. Has chooses the constructor call, and the product code holds the default.
    private static ValueTask<JsonNode?> GraphitiError(JudgeParameters p) =>
        Codec.Result(p.Has("message") ? new GraphitiException(p.String("message")) : new GraphitiException());

    private static ValueTask<JsonNode?> ChangeSetRejected(JudgeParameters p) =>
        Codec.Result(p.Has("reason") ? new ChangeSetRejectedException(p.String("reason")) : new ChangeSetRejectedException());
}
