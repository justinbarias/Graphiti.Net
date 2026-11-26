namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/datetime_utils.py</c>.</summary>
internal static class DatetimeUtilsBindings
{
    private const string Module = "graphiti_core.utils.datetime_utils:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "ensure_utc", call => call.Signature("dt").InvokeAsync(DatetimeUtils.EnsureUtc));
        table.Pure.Add(Module + "convert_datetimes_to_strings", call => call.Signature("obj").InvokeAsync(DatetimeUtils.ConvertDatetimesToStrings));
    }
}
