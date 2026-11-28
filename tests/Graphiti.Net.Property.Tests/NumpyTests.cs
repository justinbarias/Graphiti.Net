namespace Graphiti.PyCompat.Tests;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Graphiti.PyCompat;

/// <summary>
/// Tests for PyNumpy. The recorded values come from <c>python-values.numpy.json</c>, which
/// <c>port/tools/generate_numpy_values.py</c> writes with the numpy of the pinned upstream environment.
/// </summary>
public class NumpyTests
{
    private static readonly JsonElement Root = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.numpy.json"))).RootElement.GetProperty("cases");

    [Fact]
    public void AddReduce_gives_the_bits_of_numpy()
    {
        var cases = Root.GetProperty("add_reduce").EnumerateArray().ToList();
        Assert.Equal(140, cases.Count);
        foreach (var c in cases)
        {
            var values = c.GetProperty("values").EnumerateArray().Select(v => BitConverter.Int64BitsToDouble(v.GetInt64())).ToList();
            var expected = c.GetProperty("sum").GetInt64();
            var actual = BitConverter.DoubleToInt64Bits(PyNumpy.AddReduce(values));
            if (double.IsNaN(BitConverter.Int64BitsToDouble(expected)))
            {
                Assert.True(double.IsNaN(BitConverter.Int64BitsToDouble(actual)), $"length {values.Count}: numpy gives nan");
                continue;
            }

            Assert.True(expected == actual, $"length {values.Count}: numpy gives {expected}, the port gives {actual}");
        }
    }

    // python-values.numpy-float32.json: the float32 kernels, with each float32 as 8 hex digits of its bits.
    private static readonly JsonElement Float32Root = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.numpy-float32.json"))).RootElement;

    [Fact]
    public void AddReduce_of_float32_gives_the_bits_of_numpy()
    {
        var cases = Float32Root.GetProperty("cases").GetProperty("add_reduce_float32").EnumerateArray().ToList();
        Assert.Equal(92, cases.Count);
        foreach (var (index, c) in cases.Index())
        {
            var values = Floats(c.GetProperty("values").GetString()!);
            var expected = Floats(c.GetProperty("sum").GetString()!)[0];
            var actual = PyNumpy.AddReduce(values);
            if (float.IsNaN(expected))
            {
                Assert.True(float.IsNaN(actual), $"case {index}, length {values.Length}: numpy gives nan");
                continue;
            }

            Assert.True(
                BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                $"case {index}, length {values.Length}: numpy gives {expected:R}, the port gives {actual:R}");
        }
    }

    [Fact]
    public void ArgSort_gives_the_order_of_numpy()
    {
        var argsort = Float32Root.GetProperty("cases").GetProperty("argsort");
        var palette = Floats(argsort.GetProperty("palette").GetString()!);
        var digits = Float32Root.GetProperty("palette_digits").GetString()!;
        var random = argsort.GetProperty("random").EnumerateArray().ToList();
        Assert.Equal(3000, random.Count);
        List<string> failures = [];
        foreach (var (index, c) in random.Index())
        {
            var text = c.GetProperty("values").GetString()!;
            var values = text.Select(digit => palette[digits.IndexOf(digit, StringComparison.Ordinal)]).ToArray();
            var order = PyNumpy.ArgSort(values);
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join(',', order))))[..16];
            if (digest != c.GetProperty("sha").GetString())
            {
                failures.Add($"random case {index}, length {values.Length}, values {text}");
            }
        }

        var full = argsort.GetProperty("full").EnumerateArray().ToList();
        Assert.Equal(19, full.Count);
        foreach (var (index, c) in full.Index())
        {
            var values = Floats(c.GetProperty("values").GetString()!);
            var expected = string.Join(',', c.GetProperty("order").EnumerateArray().Select(i => i.GetInt32()));
            var actual = string.Join(',', PyNumpy.ArgSort(values));
            if (expected != actual)
            {
                failures.Add($"full case {index}, length {values.Length}: numpy gives {expected}, the port gives {actual}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)) + $"\n{failures.Count} failures");
    }

    private static float[] Floats(string hex) =>
        [.. Convert.FromHexString(hex).Chunk(4).Select(b => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(b)))];
}
