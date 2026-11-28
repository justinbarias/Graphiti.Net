namespace Graphiti.JudgeHost.Flow;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Graphiti.Core;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// A Python attribute path on the <c>Graphiti</c> object, resolved by the names of RULES.md R2: an attribute is the
/// PascalCase property, a method is the PascalCase method with the suffix <c>Async</c> for an <c>async def</c>, and
/// <c>search_</c> is <c>AdvancedSearchAsync</c>. A Python parameter name is the C# parameter name in snake_case. The
/// arguments are read by the rules of <see cref="JudgeTypes"/>, as <see cref="JudgeParameters.InvokeAsync"/> reads them.
/// </summary>
internal static class FlowCalls
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    // The R2 row for a name that ends in `_` beside the same name with no `_`.
    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["search_"] = "AdvancedSearchAsync",
    };

    /// <summary>One call that is bound and ready to run.</summary>
    internal sealed record BoundCall(object Target, MethodInfo Method, object?[] Arguments);

    /// <summary><c>_lookup(value, path)</c>: a list index, a dict key, or an attribute.</summary>
    internal static object? Lookup(object? value, string path)
    {
        foreach (var part in path.Split('.').Where(p => p.Length > 0))
        {
            value = Attribute(value, part);
        }

        return value;
    }

    /// <summary>Resolves the method at <paramref name="path"/> and binds the decoded arguments to its parameters.</summary>
    internal static BoundCall Bind(object graphiti, string path, List<object?> positional, OrderedDictionary<string, object?> keywords)
    {
        var dot = path.LastIndexOf('.');
        var owner = dot < 0 ? graphiti : Lookup(graphiti, path[..dot]);
        var name = path[(dot + 1)..];
        if (owner is null)
        {
            throw new MissingMemberException($"'NoneType' object has no attribute '{name}'");
        }

        var candidates = Methods(owner.GetType(), name);
        if (candidates.Count == 0)
        {
            throw new JudgeNotBoundException($"{owner.GetType().Name} has no method for the Python attribute '{name}'.");
        }

        var fitting = candidates.Where(m => Fits(m, positional, keywords)).ToList();
        var method = fitting.Count switch
        {
            1 => fitting[0],
            0 => candidates[0],
            _ => throw new JudgeHostErrorException($"{owner.GetType().Name}.{name}: {fitting.Count} overloads take the arguments of the case."),
        };
        return new BoundCall(owner, method, Arguments(method, positional, keywords));
    }

    /// <summary>Calls a bound method and awaits a task result. An exception of the method keeps its type.</summary>
    internal static async Task<object?> InvokeAsync(BoundCall call)
    {
        object? result;
        try
        {
            result = call.Method.Invoke(call.Target, call.Arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }

        var type = call.Method.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            result = type.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null);
            type = typeof(Task<>).MakeGenericType(type.GetGenericArguments());
        }
        else if (type == typeof(ValueTask))
        {
            result = ((ValueTask)result!).AsTask();
            type = typeof(Task);
        }

        if (type == typeof(Task))
        {
            await ((Task)result!).ConfigureAwait(true);
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var task = (Task)result!;
            await task.ConfigureAwait(true);
            return type.GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        return result;
    }

    /// <summary>The constructor of <see cref="GraphitiCore"/> with the four judge clients and the keywords of <c>graphiti</c>.</summary>
    internal static GraphitiCore Construct(OrderedDictionary<string, object?> keywords)
    {
        var constructor = typeof(GraphitiCore).GetConstructors(Instance).Single();
        return (GraphitiCore)constructor.Invoke(Arguments(constructor, [], keywords));
    }

    /// <summary>A snake_case Python name in PascalCase.</summary>
    internal static string Pascal(string name)
    {
        var text = new StringBuilder();
        foreach (var word in name.Split('_').Where(w => w.Length > 0))
        {
            text.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
        }

        return text.ToString();
    }

    private static object? Attribute(object? value, string part)
    {
        switch (value)
        {
            case OrderedDictionary<string, object?> dict:
                return dict.TryGetValue(part, out var item) ? item : throw new KeyNotFoundException(part);
            case IList list:
                return list[Index(part, list.Count)];
            case PyTuple tuple:
                return tuple[Index(part, tuple.Count)];
            case ITuple tuple when value.GetType().IsValueType:
                return tuple[Index(part, tuple.Length)];
            case null:
                throw new MissingMemberException($"'NoneType' object has no attribute '{part}'");
        }

        var property = value.GetType().GetProperty(Pascal(part), Instance)
            ?? throw new MissingMemberException($"'{value.GetType().Name}' object has no attribute '{part}'");
        try
        {
            return property.GetValue(value);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    // value[int(part)], with a negative index from the end.
    private static int Index(string part, int count)
    {
        var index = int.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
        index = index < 0 ? index + count : index;
        return index >= 0 && index < count ? index : throw new ArgumentOutOfRangeException(nameof(part), "list index out of range");
    }

    private static List<MethodInfo> Methods(Type type, string name)
    {
        var names = Renamed.TryGetValue(name, out var renamed) ? [renamed] : new[] { Pascal(name) + "Async", Pascal(name) };
        foreach (var candidate in names)
        {
            var found = type.GetMethods(Instance).Where(m => m.Name == candidate && !m.IsGenericMethodDefinition).ToList();
            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }

    private static ParameterInfo[] PythonParameters(MethodBase method)
    {
        var parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken) ? parameters[..^1] : parameters;
    }

    private static string PythonName(ParameterInfo parameter)
    {
        var text = new StringBuilder();
        foreach (var c in parameter.Name!)
        {
            if (char.IsUpper(c))
            {
                text.Append('_').Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static bool Fits(MethodBase method, List<object?> positional, OrderedDictionary<string, object?> keywords)
    {
        var parameters = PythonParameters(method);
        var names = parameters.Select(PythonName).ToList();
        return positional.Count <= parameters.Length && keywords.Keys.All(names.Contains);
    }

    private static object?[] Arguments(MethodBase method, List<object?> positional, OrderedDictionary<string, object?> keywords)
    {
        var all = method.GetParameters();
        var parameters = PythonParameters(method);
        if (positional.Count > parameters.Length)
        {
            throw new JudgeTypeRejectedException($"{method.Name} takes {parameters.Length} positional arguments but {positional.Count} were given");
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < positional.Count; index++)
        {
            values[PythonName(parameters[index])] = positional[index];
        }

        var names = parameters.Select(PythonName).ToList();
        foreach (var (name, value) in keywords)
        {
            if (!names.Contains(name))
            {
                throw new JudgeTypeRejectedException($"{method.Name} got an unexpected keyword argument '{name}'");
            }

            if (!values.TryAdd(name, value))
            {
                throw new JudgeTypeRejectedException($"{method.Name} got multiple values for argument '{name}'");
            }
        }

        var nullability = new NullabilityInfoContext();
        var arguments = new object?[all.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var name = names[index];
            if (!values.TryGetValue(name, out var value))
            {
                arguments[index] = parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : throw new JudgeTypeRejectedException($"{method.Name} is missing a required argument: '{name}'");
                continue;
            }

            arguments[index] = Convert(parameter, value, nullability);
        }

        if (all.Length > parameters.Length)
        {
            arguments[^1] = CancellationToken.None;
        }

        return arguments;
    }

    private static object? Convert(ParameterInfo parameter, object? value, NullabilityInfoContext nullability)
    {
        var type = parameter.ParameterType;

        // R22: an argument for a union record of Part 3 is built by its runtime type.
        if (type == typeof(SagaOrName))
        {
            return value switch
            {
                null => null,
                string name => new SagaOrName.Name { Value = name },
                SagaNode saga => new SagaOrName.Node { Saga = saga },
                _ => throw new JudgeTypeRejectedException($"argument '{parameter.Name}': the C# type SagaOrName cannot hold a Python {Py.TypeName(value)}"),
            };
        }

        var annotation = nullability.Create(parameter);
        return value is null && !type.IsValueType && annotation.WriteState == NullabilityState.Nullable
            ? null
            : JudgeTypes.Convert(type, value, $"argument '{parameter.Name}'", annotation);
    }
}
