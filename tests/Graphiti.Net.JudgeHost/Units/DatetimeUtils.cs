namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/utils/datetime_utils.py</c>.</summary>
internal static class DatetimeUtilsBindings
{
    private const string Module = "graphiti_core.utils.datetime_utils:";

    private static readonly object DateClass = new();

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "ensure_utc", call => call.Signature("dt").InvokeAsync(DatetimeUtils.EnsureUtc));
        table.Pure.Add(Module + "convert_datetimes_to_strings", call => call.Signature("obj").InvokeAsync(DatetimeUtils.ConvertDatetimesToStrings));

        // PORT: the class `datetime.date` is only a value that no C# parameter of a date type can hold. `ensure_utc` reads
        // `dt.tzinfo` on it, so the host answers JudgeTypeRejected, as Python raises AttributeError.
        table.Registry.AddRef("datetime:date", DateClass);

        // PORT: `neo4j.time.DateTime(year, ..., nanosecond)` is the value that the Neo4j driver returns for a stored datetime.
        // The Neo4j project makes the conversion to a PyDateTime that `parse_db_date` expects (R20), so the host builds the
        // naive PyDateTime that `to_native()` gives: the nanoseconds become microseconds, cut and not rounded.
        table.Registry.AddModel("neo4j.time:DateTime", data => new PyDateTime(
            (int)Py.AsLong(data["year"]),
            (int)Py.AsLong(data["month"]),
            (int)Py.AsLong(data["day"]),
            (int)Py.AsLong(data.GetValueOrDefault("hour") ?? 0L),
            (int)Py.AsLong(data.GetValueOrDefault("minute") ?? 0L),
            (int)Py.AsLong(data.GetValueOrDefault("second") ?? 0L),
            (int)(Py.AsLong(data.GetValueOrDefault("nanosecond") ?? 0L) / 1000)));
    }
}
