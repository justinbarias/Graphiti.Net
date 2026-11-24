using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Graphiti.JudgeHost.Core;

/// <summary>
/// The judge embedder. It follows port/judge/host-python/judge_clients.py step for step, in the
/// same order, so that both builds produce the same doubles.
/// </summary>
public static class JudgeEmbedding
{
    public const int Dimension = 1024;

    private static readonly Dictionary<string, double[]> Cache = [];

    public static double[] Embed(string text)
    {
        var total = new double[Dimension];
        var tokens = Tokens(text);
        if (tokens.Count == 0)
        {
            tokens.Add([]);
        }

        foreach (var token in tokens)
        {
            var vector = TokenVector(token);
            for (var index = 0; index < Dimension; index++)
            {
                total[index] += vector[index];
            }
        }

        var norm = 0.0;
        foreach (var value in total)
        {
            norm += value * value;
        }

        norm = Math.Sqrt(norm);
        if (norm == 0.0)
        {
            return total;
        }

        for (var index = 0; index < Dimension; index++)
        {
            total[index] /= norm;
        }

        return total;
    }

    /// <summary>Runs of ASCII letters, ASCII digits, and bytes over 0x7F. ASCII letters are lowercased.</summary>
    private static List<byte[]> Tokens(string text)
    {
        var tokens = new List<byte[]>();
        var current = new List<byte>();
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            if (value is >= 0x41 and <= 0x5A)
            {
                current.Add((byte)(value + 0x20));
            }
            else if (value is (>= 0x61 and <= 0x7A) or (>= 0x30 and <= 0x39) or >= 0x80)
            {
                current.Add(value);
            }
            else if (current.Count > 0)
            {
                tokens.Add([.. current]);
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            tokens.Add([.. current]);
        }

        return tokens;
    }

    private static double[] TokenVector(byte[] token)
    {
        var cacheKey = Convert.ToHexString(token);
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var seed = SHA256.HashData(token);
        var input = new byte[seed.Length + 4];
        seed.CopyTo(input, 0);
        var vector = new double[Dimension];
        for (var index = 0; index < Dimension; index++)
        {
            input[^4] = (byte)(index >> 24);
            input[^3] = (byte)(index >> 16);
            input[^2] = (byte)(index >> 8);
            input[^1] = (byte)index;
            var block = SHA256.HashData(input);
            ulong bits = 0;
            for (var position = 0; position < 8; position++)
            {
                bits = (bits << 8) | block[position];
            }

            vector[index] = ((bits >> 11) / 9007199254740992.0 * 2.0) - 1.0;
        }

        Cache[cacheKey] = vector;
        return vector;
    }

    /// <summary>Compares this implementation with the vectors that Python wrote. Returns a process exit code.</summary>
    public static int SelfCheck(string vectorsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(vectorsPath));
        var failures = 0;
        var count = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            count++;
            var text = row.GetProperty("text").GetString()!;
            var expected = row.GetProperty("vector").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var actual = Embed(text);
            var same = expected.Length == actual.Length;
            for (var index = 0; same && index < expected.Length; index++)
            {
                same = BitConverter.DoubleToInt64Bits(expected[index]) == BitConverter.DoubleToInt64Bits(actual[index]);
            }

            if (!same)
            {
                failures++;
                Console.Error.WriteLine($"embedding differs for text {JsonSerializer.Serialize(text)}");
            }
        }

        Console.WriteLine($"selfcheck: {count} vectors, {failures} differ");
        return failures == 0 && count > 0 ? 0 : 1;
    }
}
