// Probe classes for BindingsTests. Each one is a unit file with one mistake, and the loader must stop for it.
// The file holds two namespaces on purpose: the second one has the wrong name.
namespace Graphiti.JudgeHost.Units
{
    using Graphiti.JudgeHost.Core;

    internal static class GoodProbeBindings
    {
        internal static void Register(BindingTable table) => table.Pure.Add("probe:good", _ => Codec.Result("good"));
    }

    internal static class WrongNameProbe
    {
        internal static void Register(BindingTable table) => table.Pure.Add("probe:name", _ => Codec.Result("x"));
    }

    internal static class NoRegisterBindings
    {
        internal static void Add(BindingTable table) => table.Pure.Add("probe:none", _ => Codec.Result("x"));
    }

    internal static class WrongSignatureBindings
    {
        internal static void Register(Dictionary<string, string> table) => table.Add("probe:signature", "x");
    }

    internal static class ThrowingBindings
    {
        internal static void Register(BindingTable table)
        {
            table.Pure.Add("probe:throws", _ => Codec.Result("x"));
            throw new InvalidOperationException("probe");
        }
    }

    internal static class DuplicateBindings
    {
        internal static void Register(BindingTable table) => table.Pure.Add("judge:embed", _ => Codec.Result("x"));
    }
}

namespace Graphiti.JudgeHost.Unit
{
    using Graphiti.JudgeHost.Core;

    internal static class WrongNamespaceBindings
    {
        internal static void Register(BindingTable table) => table.Pure.Add("probe:namespace", _ => Codec.Result("wrong"));
    }
}
