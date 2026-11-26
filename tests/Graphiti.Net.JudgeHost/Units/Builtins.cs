namespace Graphiti.JudgeHost.Units;

using System.Collections;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// The Python builtins that judge cases call on a constant of a module: <c>str(x)</c>, <c>repr(x)</c>, and
/// <c>list(x)</c>. The argument is a <c>$ref</c> that the unit of that module registers. This file is lead code,
/// because cases of several units share these three names.
/// </summary>
internal static class BuiltinsBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("builtins:str", call => call.Signature("object").InvokeAsync((Func<object?, string>)Py.Str));
        table.Pure.Add("builtins:repr", call => call.Signature("object").InvokeAsync((Func<object?, string>)Py.Repr));
        table.Pure.Add("builtins:list", call => call.Signature("iterable").InvokeAsync((Func<object?, List<object?>>)List));
    }

    // list(x) of a tuple, a list, or a set. No case passes a str or a dict, so the host does not answer for them.
    private static List<object?> List(object? iterable) => iterable switch
    {
        string or IDictionary => throw new JudgeHostErrorException("builtins:list is bound for a tuple, a list, and a set only."),
        IEnumerable items => [.. items.Cast<object?>()],
        _ => throw new InvalidCastException($"'{iterable?.GetType().Name ?? "NoneType"}' object is not iterable"),
    };
}
