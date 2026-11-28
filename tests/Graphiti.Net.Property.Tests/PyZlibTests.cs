namespace Graphiti.PyCompat.Tests;

using System.IO.Compression;
using CsCheck;
using Graphiti.PyCompat;

// PyZlib.Decompress is an inflate of its own, because ZLibStream does not report a truncated stream as zlib does
// (GAPS F39). These tests compare it with the zlib of the runtime on valid streams. The recording of change_set
// compares it with Python on corrupt streams.
public class PyZlibTests
{
    private static readonly CompressionLevel[] Levels =
        [CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize];

    [Fact]
    public void Decompress_reads_each_stream_that_the_runtime_writes()
    {
        Gen.Select(Gen.Int[0, 4], Gen.Int[0, 70_000], Gen.Int[0, 3], Gen.Int).Sample(
            t =>
            {
                var (kind, length, level, seed) = t;
                var data = Bytes(kind, length, seed);
                Assert.Equal(data, PyZlib.Decompress(RuntimeCompress(data, Levels[level])));
            },
            iter: 400);
    }

    [Fact]
    public void Decompress_reads_what_Compress_writes_and_the_runtime_reads_it_too()
    {
        Gen.Select(Gen.Int[0, 4], Gen.Int[0, 70_000], Gen.Int).Sample(
            t =>
            {
                var (kind, length, seed) = t;
                var data = Bytes(kind, length, seed);
                var packed = PyZlib.Compress(data);
                Assert.Equal(data, PyZlib.Decompress(packed));
                using var source = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
                using var target = new MemoryStream();
                source.CopyTo(target);
                Assert.Equal(data, target.ToArray());
            },
            iter: 200);
    }

    // Kinds of input, so that each block type of deflate occurs: random bytes (stored blocks), one byte many times
    // (long matches), text of a few words (dynamic codes), a short text (fixed codes), and a mix.
    private static byte[] Bytes(int kind, int length, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[kind == 3 ? Math.Min(length, 40) : length];
        string[] words = ["episode", "entity", "edge", "é", "名前", " ", "\n", "{\"uuid\":\""];
        switch (kind)
        {
            case 0:
                rng.NextBytes(data);
                break;
            case 1:
                Array.Fill(data, (byte)rng.Next(256));
                break;
            default:
                var text = new System.Text.StringBuilder();
                while (text.Length < data.Length)
                {
                    text.Append(kind == 4 && rng.Next(8) == 0 ? ((char)rng.Next(32, 0x2000)).ToString() : words[rng.Next(words.Length)]);
                }

                var encoded = System.Text.Encoding.UTF8.GetBytes(text.ToString());
                Array.Copy(encoded, data, data.Length);
                break;
        }

        return data;
    }

    private static byte[] RuntimeCompress(byte[] data, CompressionLevel level)
    {
        using var target = new MemoryStream();
        using (var packer = new ZLibStream(target, level, leaveOpen: true))
        {
            packer.Write(data);
        }

        return target.ToArray();
    }
}
