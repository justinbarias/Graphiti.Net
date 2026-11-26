namespace Graphiti.PyCompat;

/// <summary>
/// The Python names and values of the C# enums that stand for Python <c>Enum</c> classes (RULES.md R7).
/// A C# enum member has a PascalCase name, and its Python name and wire value cannot be derived from it.
/// <see cref="Py.Repr"/>, <see cref="Py.Str"/>, <see cref="Py.Format"/>, and <see cref="Py.EnumValue{TEnum}"/>
/// read them here. Each enum registers one time, before a value of it reaches one of those helpers.
/// Only a plain <c>Enum</c> is supported: a member equals only itself and is always true.
/// </summary>
internal static class PyEnum
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, EnumInfo> Infos = [];

    /// <summary>
    /// Registers a C# enum with the Python class name, and the Python name and the value of each member.
    /// A second registration of the same enum replaces the first.
    /// </summary>
    internal static void Register<TEnum>(string pythonClassName, params (TEnum Member, string Name, object Value)[] members)
        where TEnum : struct, Enum
    {
        var info = new EnumInfo(pythonClassName, members.ToDictionary(m => (Enum)m.Member, m => (m.Name, m.Value)));
        lock (Gate)
        {
            Infos[typeof(TEnum)] = info;
        }
    }

    /// <summary>The Python class name of a registered enum.</summary>
    internal static string ClassName(Enum member) => Info(member).ClassName;

    /// <summary>The Python member name, as <c>member.name</c> gives it.</summary>
    internal static string Name(Enum member) => Member(member).Name;

    /// <summary>The wire value, as <c>member.value</c> gives it.</summary>
    internal static object Value(Enum member) => Member(member).Value;

    private static (string Name, object Value) Member(Enum member) =>
        Info(member).Members.TryGetValue(member, out var m)
            ? m
            : throw new InvalidOperationException($"PyEnum: {member.GetType().FullName} has no registered member {member}");

    private static EnumInfo Info(Enum member)
    {
        lock (Gate)
        {
            return Infos.TryGetValue(member.GetType(), out var info)
                ? info
                : throw new InvalidOperationException($"PyEnum: the enum {member.GetType().FullName} is not registered");
        }
    }

    private sealed record EnumInfo(string ClassName, Dictionary<Enum, (string Name, object Value)> Members);
}
