using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PythonValues;

namespace Graphiti.PyCompat.Tests;

public class NumberTests
{
    [Fact]
    public void TrueDiv_ints_match_Python() => CheckFloat("true_div_int", c => Py.TrueDiv(c.GetProperty("a").GetInt64(), c.GetProperty("b").GetInt64()));

    [Fact]
    public void TrueDiv_floats_match_Python() => CheckFloat("true_div_float", c => Py.TrueDiv(Bits(c, "a"), Bits(c, "b")));

    [Fact]
    public void FloorDiv_matches_Python() => CheckLong("floor_div", c => Py.FloorDiv(c.GetProperty("a").GetInt64(), c.GetProperty("b").GetInt64()));

    [Fact]
    public void Mod_matches_Python() => CheckLong("mod", c => Py.Mod(c.GetProperty("a").GetInt64(), c.GetProperty("b").GetInt64()));

    [Fact]
    public void Int_of_float_matches_Python() => CheckLong("int_float", c => Py.Int(Bits(c, "x")));

    [Fact]
    public void Int_of_text_matches_Python() => CheckLong("int_str", c => Py.Int(Text(c, "s")));

    [Fact]
    public void Float_of_text_matches_Python() => CheckFloat("float_str", c => Py.Float(Text(c, "s")));

    // Laws that hold for any input. They do not use the Python values.

    [Fact]
    public void FloorDiv_and_Mod_split_a_number()
    {
        var gen = Gen.Select(Gen.Long[-1_000_000_000, 1_000_000_000], Gen.Long[-1_000_000, 1_000_000].Where(b => b != 0));
        gen.Sample((a, b) =>
        {
            var q = Py.FloorDiv(a, b);
            var r = Py.Mod(a, b);
            return q * b + r == a && Math.Abs(r) < Math.Abs(b) && (r == 0 || (r < 0) == (b < 0));
        });
    }

    [Fact]
    public void Zero_divisor_always_throws()
    {
        Gen.Long.Sample(a =>
        {
            Assert.Throws<DivideByZeroException>(() => Py.FloorDiv(a, 0));
            Assert.Throws<DivideByZeroException>(() => Py.Mod(a, 0));
            Assert.Throws<DivideByZeroException>(() => Py.TrueDiv(a, 0));
            Assert.Throws<DivideByZeroException>(() => Py.TrueDiv((double)a, 0.0));
        });
    }

    [Fact]
    public void TrueDiv_of_ints_is_the_nearest_double_to_the_exact_quotient()
    {
        // The result is no farther from a / b than the double below it or the double above it.
        // The comparison uses exact integer arithmetic: each double is mantissa * 2^exponent.
        Gen.Select(Gen.Long, Gen.Long.Where(b => b != 0)).Sample((a, b) =>
        {
            var r = Py.TrueDiv(a, b);
            var error = ScaledError(r, a, b);
            return error <= ScaledError(Math.BitDecrement(r), a, b) && error <= ScaledError(Math.BitIncrement(r), a, b);
        });
    }

    /// <summary>|x - a / b| * |b| * 2^1100, exact. A double is an integer times a power of two.</summary>
    private static System.Numerics.BigInteger ScaledError(double x, long a, long b)
    {
        var bits = BitConverter.DoubleToInt64Bits(x);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        var value = new System.Numerics.BigInteger(mantissa) << (exponent - 1075 + 1100);
        if (bits < 0)
        {
            value = -value;
        }

        return System.Numerics.BigInteger.Abs((value * b) - (new System.Numerics.BigInteger(a) << 1100));
    }

    [Fact]
    public void Int_of_a_float_cuts_toward_zero()
    {
        Gen.Double[-1e15, 1e15].Sample(x =>
        {
            var n = Py.Int(x);
            return Math.Abs((double)n) <= Math.Abs(x) && Math.Abs(x) - Math.Abs((double)n) < 1 && (n == 0 || (n < 0) == (x < 0));
        });
    }
}
