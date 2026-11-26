namespace Graphiti.PyCompat.Tests;

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
}
