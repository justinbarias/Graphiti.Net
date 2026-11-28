namespace Graphiti.JudgeHost.Units;

using global::Neo4j.Driver;
using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;
using Graphiti.Neo4j;

/// <summary>The bindings of <c>graphiti_core/utils/datetime_utils.py</c>.</summary>
internal static class DatetimeUtilsBindings
{
    private const string Module = "graphiti_core.utils.datetime_utils:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "ensure_utc", call => call.Signature("dt").InvokeAsync(DatetimeUtils.EnsureUtc));
        table.Pure.Add(Module + "convert_datetimes_to_strings", call => call.Signature("obj").InvokeAsync(DatetimeUtils.ConvertDatetimesToStrings));

        // PORT: the class `datetime.date`, as a class that a result or an `object?` parameter holds. `ensure_utc` reads
        // `dt.tzinfo` on it, so no C# parameter of a date type can hold it, and the host answers JudgeTypeRejected, as
        // Python raises AttributeError.
        table.Registry.AddRef("datetime:date", new JudgeClass("date"));

        // PORT: `neo4j.time.DateTime(year, month, day, hour, minute, second, nanosecond)` is the value that the Neo4j driver
        // returns for a stored datetime. The host builds the `Neo4j.Driver.LocalDateTime` of the same fields and gives it to
        // `Neo4jDriver.FromDriverValue`, which is the conversion of the product (R9, R20): it gives the naive PyDateTime
        // that `to_native()` gives. A key that the class does not take is a TypeError, as in Python. A value that the
        // constructor of `LocalDateTime` rejects reaches the caller with its own exception.
        table.Registry.AddModel("neo4j.time:DateTime", data =>
        {
            string[] names = ["year", "month", "day", "hour", "minute", "second", "nanosecond"];
            foreach (var key in data.Keys)
            {
                if (!names.Contains(key, StringComparer.Ordinal))
                {
                    throw new InvalidCastException($"DateTime() got an unexpected keyword argument '{key}'");
                }
            }

            int Field(string name, int fallback) =>
                data.TryGetValue(name, out var value)
                    ? (int)JudgeTypes.Convert(typeof(int), value, $"neo4j.time:DateTime '{name}'")!
                    : name is "year" or "month" or "day"
                        ? throw new InvalidCastException($"DateTime() missing required argument: '{name}'")
                        : fallback;

            return Neo4jDriver.FromDriverValue(new LocalDateTime(
                Field("year", 0),
                Field("month", 0),
                Field("day", 0),
                Field("hour", 0),
                Field("minute", 0),
                Field("second", 0),
                Field("nanosecond", 0)));
        });
    }
}
