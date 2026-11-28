// The public surface as a list (plan 0004, layer 9, packet P9d). One test builds the sorted list of each public type
// and member of the two product assemblies and compares it with PublicSurface.txt next to this file. A reviewer sees
// each change of the public surface as a diff of that file. On a difference the test writes the new list to
// PublicSurface.actual.txt in the output folder.
namespace Graphiti.E2E.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Graphiti.Internal;
using Core = Graphiti.Core;
using CoreSearch = Graphiti.Core.Search;

public sealed class PublicSurfaceTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly NullabilityInfoContext Nullability = new();

    [Fact]
    public void The_public_surface_equals_the_checked_in_list()
    {
        var actual = Surface(typeof(IGraphiti).Assembly, typeof(Neo4jOptions).Assembly);
        var expected = File.ReadAllText(Path.Combine(SourceFolder(), "PublicSurface.txt"), Encoding.UTF8).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "PublicSurface.actual.txt"), actual, Encoding.UTF8);
            var expectedLines = expected.Split('\n').ToHashSet(StringComparer.Ordinal);
            var actualLines = actual.Split('\n').ToHashSet(StringComparer.Ordinal);
            Assert.Fail(
                "The public surface changed. Lines that are new:\n"
                + string.Join("\n", actualLines.Except(expectedLines).Take(40))
                + "\nLines that are gone:\n"
                + string.Join("\n", expectedLines.Except(actualLines).Take(40)));
        }
    }

    [Fact]
    public void Each_public_enum_maps_to_the_ported_member_of_the_same_name()
    {
        AssertMapsByName<EpisodeType, Core.EpisodeType>(PublicMapping.ToPorted);
        AssertMapsByName<Core.EpisodeType, EpisodeType>(PublicMapping.ToPublic);
        AssertMapsByName<EdgeSearchMethod, CoreSearch.EdgeSearchMethod>(PublicMapping.ToPorted);
        AssertMapsByName<NodeSearchMethod, CoreSearch.NodeSearchMethod>(PublicMapping.ToPorted);
        AssertMapsByName<EpisodeSearchMethod, CoreSearch.EpisodeSearchMethod>(PublicMapping.ToPorted);
        AssertMapsByName<CommunitySearchMethod, CoreSearch.CommunitySearchMethod>(PublicMapping.ToPorted);
        AssertMapsByName<EdgeReranker, CoreSearch.EdgeReranker>(PublicMapping.ToPorted);
        AssertMapsByName<NodeReranker, CoreSearch.NodeReranker>(PublicMapping.ToPorted);
        AssertMapsByName<EpisodeReranker, CoreSearch.EpisodeReranker>(PublicMapping.ToPorted);
        AssertMapsByName<CommunityReranker, CoreSearch.CommunityReranker>(PublicMapping.ToPorted);
        AssertMapsByName<ComparisonOperator, CoreSearch.ComparisonOperator>(PublicMapping.ToPorted);
    }

    internal static string Surface(params Assembly[] assemblies)
    {
        var lines = new List<string>();
        foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            lines.Add("assembly " + assembly.GetName().Name);
            foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                lines.Add(Header(type));
                lines.AddRange(Members(type).Order(StringComparer.Ordinal).Select(m => "  " + m));
            }
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string Header(Type type)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : typeof(Delegate).IsAssignableFrom(type) ? "delegate"
            : (type.IsAbstract && type.IsSealed) ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";
        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(Enum)
            && baseType != typeof(ValueType) && baseType != typeof(MulticastDelegate))
        {
            bases.Add(Name(baseType));
        }

        bases.AddRange(type.GetInterfaces().Select(Name).Order(StringComparer.Ordinal));
        return $"{kind} {Name(type)}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty);
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return $"value {field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)}";
            }

            yield break;
        }

        foreach (var member in type.GetMembers(Declared))
        {
            switch (member)
            {
                case ConstructorInfo ctor when Visible(ctor):
                    yield return $"{Access(ctor)}ctor({Parameters(ctor)})";
                    break;
                case MethodInfo method when Visible(method) && (!method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)):
                    var generic = method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(Name)) + ">" : string.Empty;
                    yield return $"{Access(method)}{Modifiers(method)}method {Annotated(method.ReturnType, Nullability.Create(method.ReturnParameter))} {method.Name}{generic}({Parameters(method)})";
                    break;
                case PropertyInfo property when Accessors(property).Any(Visible):
                    var required = property.IsDefined(typeof(RequiredMemberAttribute), false) ? "required " : string.Empty;
                    var getter = property.GetMethod is { } get && Visible(get) ? " get;" : string.Empty;
                    var setter = property.SetMethod is { } set && Visible(set)
                        ? set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit") ? " init;" : " set;"
                        : string.Empty;
                    var accessor = Accessors(property).First(Visible);
                    var indexer = property.GetIndexParameters().Length > 0 ? "[" + Parameters(property.GetIndexParameters()) + "]" : string.Empty;
                    yield return $"{Access(accessor)}{Modifiers(accessor)}{required}property {Annotated(property.PropertyType, Nullability.Create(property))} {property.Name}{indexer} {{{getter}{setter} }}";
                    break;
                case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                    yield return $"{(field.IsStatic ? "static " : string.Empty)}{(field.IsInitOnly ? "readonly " : string.Empty)}field {Annotated(field.FieldType, Nullability.Create(field))} {field.Name}";
                    break;
                case EventInfo @event when @event.AddMethod is { } add && Visible(add):
                    yield return $"event {Name(@event.EventHandlerType!)} {@event.Name}";
                    break;
            }
        }
    }

    private static IEnumerable<MethodInfo> Accessors(PropertyInfo property) =>
        new[] { property.GetMethod, property.SetMethod }.OfType<MethodInfo>();

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string Access(MethodBase method) => method.IsPublic ? string.Empty : "protected ";

    private static string Modifiers(MethodInfo method) =>
        method.IsStatic ? "static "
        : method.IsAbstract && !method.DeclaringType!.IsInterface ? "abstract "
        : method.IsVirtual && !method.IsFinal && !method.DeclaringType!.IsInterface
            ? method.GetBaseDefinition() == method ? "virtual " : "override "
            : string.Empty;

    private static string Parameters(MethodBase method) => Parameters(method.GetParameters());

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p =>
    {
        var text = $"{Annotated(p.ParameterType, Nullability.Create(p))} {p.Name}";
        if (p.HasDefaultValue)
        {
            text += " = " + (p.DefaultValue switch
            {
                null => "null",
                string s => "\"" + s + "\"",
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                var other => other.ToString(),
            });
        }

        return text;
    }));

    private static string Annotated(Type type, NullabilityInfo info) =>
        Name(type) + (!type.IsValueType && info.ReadState == NullabilityState.Nullable ? "?" : string.Empty);

    private static string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (type.IsByRef)
        {
            return Name(type.GetElementType()!) + "&";
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : type.Namespace + "." + type.Name;
        if (type.IsGenericType)
        {
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            var arguments = type.IsNested ? type.GetGenericArguments().Skip(type.DeclaringType!.GetGenericArguments().Length) : type.GetGenericArguments();
            name = (tick < 0 ? name : name[..tick]) + "<" + string.Join(", ", arguments.Select(Name)) + ">";
        }

        return name;
    }

    private static void AssertMapsByName<TFrom, TTo>(Func<TFrom, TTo> map)
        where TFrom : struct, Enum
        where TTo : struct, Enum
    {
        Assert.Equal(Enum.GetNames<TFrom>().Length, Enum.GetNames<TTo>().Length);
        foreach (var value in Enum.GetValues<TFrom>())
        {
            Assert.Equal(Enum.GetName(value), Enum.GetName(map(value)));
        }
    }

    private static string SourceFolder([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
