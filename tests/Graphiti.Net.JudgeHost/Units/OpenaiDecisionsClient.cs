namespace Graphiti.JudgeHost.Units;

/// <summary>
/// The bindings of <c>graphiti_core/llm_client/openai_decisions_client.py</c>. The judge has no case for this module:
/// its constructor takes an HTTP client, and its methods send requests, which the value model cannot hold. The unit has
/// its own tests in <c>tests/Port.Evaluation</c>.
/// </summary>
internal static class OpenaiDecisionsClientBindings
{
    internal static void Register(BindingTable table)
    {
        _ = table;
    }
}
