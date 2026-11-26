namespace Graphiti.JudgeHost.Units;

/// <summary>
/// The bindings of <c>graphiti_core/tracer.py</c>. The judge has no case for this module: no scenario reaches the
/// tracer (GAPS J13), and its functions take a tracer object or return one, which the value model cannot hold. The
/// unit has its own recording in <c>tests/Port.Evaluation</c>.
/// </summary>
internal static class TracerBindings
{
    internal static void Register(BindingTable table)
    {
        _ = table;
    }
}
