namespace Graphiti.PyCompat;

using System.Numerics;
using System.Security.Cryptography;

/// <summary>
/// The <c>random</c> functions that upstream needs: <c>random.sample(range(n), k)</c> in
/// <c>content_chunking.py</c>, and <c>random.uniform</c> and <c>random()</c> for the retry wait (R16, J3).
/// GAPS F9 asks for a source that a test can seed, with the same distribution as Python. It does not
/// ask for the same sequence. So the generator is xoshiro256** seeded with SplitMix64, written here,
/// because the sequence of <see cref="System.Random"/> can change between .NET versions.
/// </summary>
internal sealed class PyRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>A source with a fixed seed. The same seed gives the same sequence.</summary>
    internal PyRandom(long seed) => Seed(unchecked((ulong)seed));

    /// <summary>A source seeded from the operating system.</summary>
    internal PyRandom() => Seed(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    /// <summary>The next 64 random bits (xoshiro256**).</summary>
    internal ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary><c>random.random()</c>: a float in [0, 1) with 53 random bits.</summary>
    internal double Random() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary><c>random.uniform(a, b)</c>: <c>a + (b - a) * random()</c>.</summary>
    internal double Uniform(double a, double b) => a + ((b - a) * Random());

    /// <summary>
    /// <c>random.sample(range(n), k)</c>: k distinct integers in [0, n) in random order. A negative n counts as 0, as in <c>len(range(n))</c>. A k below 0
    /// or above n throws <see cref="ArgumentException"/> (<c>ValueError</c>).
    /// </summary>
    internal long[] SampleRange(long n, int k)
    {
        if (k < 0 || k > Math.Max(n, 0))
        {
            throw new ArgumentException("Sample larger than population or is negative");
        }

        // A partial Fisher-Yates shuffle over a virtual array. Only moved slots are stored,
        // so the cost depends on k and not on n.
        var moved = new Dictionary<long, long>();
        var result = new long[k];
        for (var i = 0; i < k; i++)
        {
            var j = i + (long)RandBelow((ulong)(n - i));
            result[i] = moved.GetValueOrDefault(j, j);
            moved[j] = moved.GetValueOrDefault(i, i);
        }

        return result;
    }

    // An integer in [0, n) with no bias: draw the top bits, and draw again for a value that is too large.
    private ulong RandBelow(ulong n)
    {
        var bits = 64 - BitOperations.LeadingZeroCount(n - 1 | 1);
        ulong r;
        do
        {
            r = NextUInt64() >> (64 - bits);
        }
        while (r >= n);
        return r;
    }

    private void Seed(ulong seed)
    {
        var x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
