namespace Graphiti.PyCompat;

using System.Security.Cryptography;

internal static partial class Py
{
    /// <summary><c>uuid.NAMESPACE_URL</c>.</summary>
    internal static readonly Guid NamespaceUrl = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary><c>str(uuid4())</c>: a random version 4 UUID in lower case with hyphens.</summary>
    internal static string Uuid4() => Guid.NewGuid().ToString("D");

    /// <summary><c>uuid4().hex</c>: a random version 4 UUID in lower case with no hyphens.</summary>
    internal static string Uuid4Hex() => Guid.NewGuid().ToString("N");

    /// <summary><c>time.time()</c>: Unix seconds with a fraction, from the system clock.</summary>
    internal static double TimeSeconds() =>
        (TimeProvider.System.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary>
    /// <c>str(uuid5(namespace, name))</c>: the SHA-1 UUID of the namespace and the UTF-8 name.
    /// A name with a lone surrogate throws <see cref="System.Text.EncoderFallbackException"/> (<c>UnicodeEncodeError</c>).
    /// </summary>
    internal static string Uuid5(Guid namespaceUuid, string name)
    {
        var nameBytes = PyText.EncodeUtf8(name);
        var input = new byte[16 + nameBytes.Length];
        namespaceUuid.TryWriteBytes(input, bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);
        var hash = SHA1.HashData(input);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString("D");
    }
}
