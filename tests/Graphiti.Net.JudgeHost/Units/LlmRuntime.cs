namespace Graphiti.JudgeHost.Units;

/// <summary>
/// The bindings of <c>graphiti_core/llm_client/llm_runtime.py</c>. The judge has no case for this module: its
/// constructor takes a transport that holds a model client, and its methods call the client, which the value model
/// cannot hold. The unit has its own tests in <c>tests/Port.Evaluation</c>.
/// </summary>
internal static class LlmRuntimeBindings
{
    internal static void Register(BindingTable table)
    {
        _ = table;
    }
}
