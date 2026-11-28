namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Ingestion;
using Graphiti.PyCompat;

/// <summary>
/// The bindings of <c>graphiti_core/ingestion/state_store.py</c>. The module has no judge case of its own. The cases of
/// <c>ingestion/delta.py</c> pass a <c>DeltaUnit</c> as a <c>$model</c> argument, so the module that defines the class
/// registers it (R22).
/// </summary>
internal static class StateStoreBindings
{
    private const string Module = "graphiti_core.ingestion.state_store";

    internal static void Register(BindingTable table)
    {
        // `DeltaUnit` is a frozen dataclass: `DeltaUnit(**data)` stores each value as it is.
        table.Registry.AddModel(Module + ":DeltaUnit", data => new DeltaUnit
        {
            Seq = Py.AsLong(data["seq"]),
            Status = Py.AsString(data["status"]),
            Payload = Py.AsBytes(data["payload"]),
        });
    }
}
