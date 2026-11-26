namespace Graphiti.PyCompat;

using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// The Python member name and value of one member of a C# enum that stands for a Python <c>Enum</c>
/// class (RULES.md R7): <c>[PyEnumMember("message", "message")]</c>. The value is a string or a long.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class PyEnumMemberAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="PyEnumMemberAttribute"/> class for a member with a string value.</summary>
    internal PyEnumMemberAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="PyEnumMemberAttribute"/> class for a member with an int value.</summary>
    internal PyEnumMemberAttribute(string name, long value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets the Python member name, as <c>member.name</c> gives it.</summary>
    internal string Name { get; }

    /// <summary>Gets the wire value, as <c>member.value</c> gives it.</summary>
    internal object Value { get; }
}

/// <summary>
/// The Python view of a C# enum: the class name is the C# type name (R2), and each member carries a
/// <see cref="PyEnumMemberAttribute"/>. The attributes of a type are read one time and cached.
/// Only a plain <c>Enum</c> is supported: a member equals only itself and is always true.
/// </summary>
internal static class PyEnum
{
    private static readonly ConcurrentDictionary<Type, EnumInfo> Infos = new();

    /// <summary>The Python class name: the C# type name.</summary>
    internal static string ClassName(Enum member) => member.GetType().Name;

    /// <summary>The Python member name, as <c>member.name</c> gives it.</summary>
    internal static string Name(Enum member) => Member(member).Name;

    /// <summary>The wire value, as <c>member.value</c> gives it.</summary>
    internal static object Value(Enum member) => Member(member).Value;

    /// <summary>
    /// <c>Cls(value)</c>: the member whose value equals <paramref name="value"/> by Python <c>==</c>, in member
    /// order. No such member throws <see cref="ArgumentException"/> (<c>ValueError</c>) with Python's message.
    /// </summary>
    internal static T FromValue<T>(object? value)
        where T : struct, Enum
    {
        foreach (var (member, attribute) in Info(typeof(T)).Members)
        {
            if (PyValueEquality.Equal(attribute.Value, value))
            {
                return (T)member;
            }
        }

        throw new ArgumentException($"{Py.Repr(value)} is not a valid {typeof(T).Name}");
    }

    private static PyEnumMemberAttribute Member(Enum member)
    {
        foreach (var (m, attribute) in Info(member.GetType()).Members)
        {
            if (m.Equals(member))
            {
                return attribute;
            }
        }

        throw new InvalidOperationException($"PyEnum: {member.GetType().FullName} has no member with the value {member:D}");
    }

    private static EnumInfo Info(Type type) => Infos.GetOrAdd(type, Read);

    private static EnumInfo Read(Type type)
    {
        var members = new List<(Enum Member, PyEnumMemberAttribute Attribute)>();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<PyEnumMemberAttribute>()
                ?? throw new InvalidOperationException($"PyEnum: the member {type.FullName}.{field.Name} has no PyEnumMember attribute");
            if (field.GetValue(null) is not Enum member)
            {
                throw new InvalidOperationException($"PyEnum: {type.FullName}.{field.Name} is not an enum member");
            }

            members.Add((member, attribute));
        }

        return new EnumInfo(members);
    }

    private sealed record EnumInfo(List<(Enum Member, PyEnumMemberAttribute Attribute)> Members);
}
