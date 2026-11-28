namespace Graphiti.PyCompat;

using System.IO.Compression;

/// <summary>
/// <c>zlib.compress(data)</c> and <c>zlib.decompress(data)</c>.
///
/// <see cref="Compress"/> writes a valid zlib stream at level 6, the default of Python. .NET compresses with zlib-ng,
/// CPython with the system zlib, so the bytes can differ. Each side reads the bytes of the other.
///
/// <see cref="Decompress"/> is a full inflate, because <c>ZLibStream</c> does not report where the stream ends and does not
/// give the message of zlib. It checks the header, each block, the end of the stream, and the Adler-32 value, in the order
/// of zlib, and it throws <see cref="InvalidDataException"/> for each input that Python refuses, with the text of
/// <c>zlib.error</c> in most cases. For a small part of the corrupt inputs the text differs (decision D51): the zlib of
/// the oracle reports <c>invalid literal/length/distance code</c> where this reader reports
/// <c>invalid literal/length code</c> or <c>invalid distance code</c>. Bytes after the end of the stream are ignored, as
/// <c>zlib.decompress</c> ignores them.
/// </summary>
internal static class PyZlib
{
    private const int MaxBits = 15;

    private static readonly ushort[] LengthBase =
    [
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
    ];

    private static readonly byte[] LengthExtra =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
    ];

    private static readonly ushort[] DistanceBase =
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577,
    ];

    private static readonly byte[] DistanceExtra =
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
    ];

    private static readonly byte[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private static readonly Huffman FixedLengths = Huffman.Build(FixedLengthCodeLengths(), codeLengths: false)
        ?? throw new InvalidOperationException("PyZlib: the fixed literal/length code is complete");

    private static readonly Huffman FixedDistances = Huffman.Build(Enumerable.Repeat((byte)5, 32).ToArray(), codeLengths: false)
        ?? throw new InvalidOperationException("PyZlib: the fixed distance code is complete");

    /// <summary><c>zlib.compress(data)</c>, with the default level 6.</summary>
    internal static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, new ZLibCompressionOptions { CompressionLevel = 6 }, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>
    /// <c>zlib.decompress(data)</c>. Each input for which Python raises <c>zlib.error</c> throws
    /// <see cref="InvalidDataException"/>, in most cases with the same text, for example
    /// <c>Error -5 while decompressing data: incomplete or truncated stream</c>. For a small part of the corrupt inputs
    /// the text names a different invalid code than the zlib of the oracle (decision D51).
    /// </summary>
    internal static byte[] Decompress(byte[] data) => new Inflater(data).Run();

    private static byte[] FixedLengthCodeLengths()
    {
        var lengths = new byte[288];
        for (var symbol = 0; symbol < 288; symbol++)
        {
            lengths[symbol] = (byte)(symbol < 144 ? 8 : symbol < 256 ? 9 : symbol < 280 ? 7 : 8);
        }

        return lengths;
    }

    private static InvalidDataException Truncated() =>
        new("Error -5 while decompressing data: incomplete or truncated stream");

    private static InvalidDataException Corrupt(string reason) => new($"Error -3 while decompressing data: {reason}");

    /// <summary>A canonical Huffman code, as <c>inflate_table</c> of zlib accepts it.</summary>
    private sealed class Huffman
    {
        private readonly short[] count = new short[MaxBits + 1];

        private short[] symbol = [];

        // One code of length 1: the code 0 is the symbol, and the code 1 is not a code.
        private bool single;

        // No code at all.
        private bool empty;

        /// <summary>
        /// The code of these lengths, or null for a set that zlib refuses: more codes than the lengths allow, or a code
        /// that does not use all of them. The two exceptions are the same as in zlib: a set with no code, and (not for
        /// the code length code) a set with one code of length 1.
        /// </summary>
        internal static Huffman? Build(byte[] lengths, bool codeLengths)
        {
            var code = new Huffman();
            foreach (var length in lengths)
            {
                code.count[length]++;
            }

            var max = MaxBits;
            while (max >= 1 && code.count[max] == 0)
            {
                max--;
            }

            if (max == 0)
            {
                code.empty = true;
                return code;
            }

            var left = 1;
            for (var length = 1; length <= MaxBits; length++)
            {
                left <<= 1;
                left -= code.count[length];
                if (left < 0)
                {
                    return null;
                }
            }

            if (left > 0 && (codeLengths || max != 1))
            {
                return null;
            }

            code.single = left > 0;
            var offsets = new short[MaxBits + 2];
            for (var length = 1; length <= MaxBits; length++)
            {
                offsets[length + 1] = (short)(offsets[length] + code.count[length]);
            }

            code.symbol = new short[lengths.Length];
            for (var index = 0; index < lengths.Length; index++)
            {
                if (lengths[index] != 0)
                {
                    code.symbol[offsets[lengths[index]]++] = (short)index;
                }
            }

            return code;
        }

        /// <summary>The symbol of the next code, or -1 for a bit pattern that is no code. The code length code has no -1.</summary>
        internal int Decode(Inflater source, bool codeLengths)
        {
            if (empty)
            {
                source.Bits(1);
                return codeLengths ? 0 : -1;
            }

            if (single)
            {
                return source.Bits(1) == 0 ? symbol[0] : -1;
            }

            var code = 0;
            var first = 0;
            var index = 0;
            for (var length = 1; length <= MaxBits; length++)
            {
                code |= source.Bits(1);
                var n = count[length];
                if (code - n < first)
                {
                    return symbol[index + (code - first)];
                }

                index += n;
                first += n;
                first <<= 1;
                code <<= 1;
            }

            return -1;
        }
    }

    /// <summary>One run of inflate. It reads a byte only when it needs the bits, as zlib does, so the end of the stream is exact.</summary>
    private sealed class Inflater(byte[] input)
    {
        private int position;
        private int bitBuffer;
        private int bitCount;
        private byte[] output = new byte[Math.Max(input.Length * 4, 256)];
        private int outputLength;

        internal byte[] Run()
        {
            Header();
            bool last;
            do
            {
                var block = Bits(3);
                last = (block & 1) == 1;
                switch (block >> 1)
                {
                    case 0:
                        Stored();
                        break;
                    case 1:
                        Codes(FixedLengths, FixedDistances);
                        break;
                    case 2:
                        Dynamic();
                        break;
                    default:
                        throw Corrupt("invalid block type");
                }
            }
            while (!last);

            // The bits of the last byte of the stream are dropped, then the Adler-32 value follows, in big-endian order.
            bitBuffer = 0;
            bitCount = 0;
            if (position + 4 > input.Length)
            {
                throw Truncated();
            }

            var check = ((uint)input[position] << 24) | ((uint)input[position + 1] << 16) | ((uint)input[position + 2] << 8) | input[position + 3];
            if (check != Adler32())
            {
                throw Corrupt("incorrect data check");
            }

            return output.AsSpan(0, outputLength).ToArray();
        }

        /// <summary>The next <paramref name="count"/> bits, as a number. The first bit is the lowest.</summary>
        internal int Bits(int count)
        {
            while (bitCount < count)
            {
                if (position >= input.Length)
                {
                    throw Truncated();
                }

                bitBuffer |= input[position++] << bitCount;
                bitCount += 8;
            }

            var value = bitBuffer & ((1 << count) - 1);
            bitBuffer >>= count;
            bitCount -= count;
            return value;
        }

        private void Header()
        {
            if (input.Length < 2)
            {
                throw Truncated();
            }

            var method = input[0];
            var flags = input[1];
            if (((method << 8) + flags) % 31 != 0)
            {
                throw Corrupt("incorrect header check");
            }

            if ((method & 15) != 8)
            {
                throw Corrupt("unknown compression method");
            }

            if ((method >> 4) + 8 > MaxBits)
            {
                throw Corrupt("invalid window size");
            }

            position = 2;
            if ((flags & 0x20) != 0)
            {
                // A stream that needs a preset dictionary: zlib.decompress has none, and Python raises "Error 2".
                if (input.Length < 6)
                {
                    throw Truncated();
                }

                throw new InvalidDataException("Error 2 while decompressing data");
            }
        }

        private void Stored()
        {
            bitBuffer = 0;
            bitCount = 0;
            if (position + 4 > input.Length)
            {
                throw Truncated();
            }

            var length = input[position] | (input[position + 1] << 8);
            var complement = input[position + 2] | (input[position + 3] << 8);
            if (length != (complement ^ 0xFFFF))
            {
                throw Corrupt("invalid stored block lengths");
            }

            position += 4;
            if (position + length > input.Length)
            {
                throw Truncated();
            }

            Reserve(length);
            Array.Copy(input, position, output, outputLength, length);
            outputLength += length;
            position += length;
        }

        private void Dynamic()
        {
            var header = Bits(14);
            var literalCount = (header & 31) + 257;
            var distanceCount = ((header >> 5) & 31) + 1;
            var codeLengthCount = ((header >> 10) & 15) + 4;
            if (literalCount > 286 || distanceCount > 30)
            {
                throw Corrupt("too many length or distance symbols");
            }

            var codeLengthLengths = new byte[19];
            for (var index = 0; index < codeLengthCount; index++)
            {
                codeLengthLengths[CodeLengthOrder[index]] = (byte)Bits(3);
            }

            var codeLengthCode = Huffman.Build(codeLengthLengths, codeLengths: true) ?? throw Corrupt("invalid code lengths set");

            var total = literalCount + distanceCount;
            var lengths = new byte[total];
            var have = 0;
            while (have < total)
            {
                var symbol = codeLengthCode.Decode(this, codeLengths: true);
                if (symbol < 16)
                {
                    lengths[have++] = (byte)symbol;
                    continue;
                }

                byte value = 0;
                int repeat;
                if (symbol == 16)
                {
                    var extra = Bits(2);
                    if (have == 0)
                    {
                        throw Corrupt("invalid bit length repeat");
                    }

                    value = lengths[have - 1];
                    repeat = 3 + extra;
                }
                else if (symbol == 17)
                {
                    repeat = 3 + Bits(3);
                }
                else
                {
                    repeat = 11 + Bits(7);
                }

                if (have + repeat > total)
                {
                    throw Corrupt("invalid bit length repeat");
                }

                while (repeat-- > 0)
                {
                    lengths[have++] = value;
                }
            }

            if (lengths[256] == 0)
            {
                throw Corrupt("invalid code -- missing end-of-block");
            }

            var literals = Huffman.Build(lengths[..literalCount], codeLengths: false) ?? throw Corrupt("invalid literal/lengths set");
            var distances = Huffman.Build(lengths[literalCount..], codeLengths: false) ?? throw Corrupt("invalid distances set");
            Codes(literals, distances);
        }

        private void Codes(Huffman literals, Huffman distances)
        {
            while (true)
            {
                var symbol = literals.Decode(this, codeLengths: false);
                if (symbol < 0)
                {
                    throw Corrupt("invalid literal/length code");
                }

                if (symbol < 256)
                {
                    Reserve(1);
                    output[outputLength++] = (byte)symbol;
                    continue;
                }

                if (symbol == 256)
                {
                    return;
                }

                symbol -= 257;
                if (symbol >= LengthBase.Length)
                {
                    throw Corrupt("invalid literal/length code");
                }

                var length = LengthBase[symbol] + Bits(LengthExtra[symbol]);
                var distanceSymbol = distances.Decode(this, codeLengths: false);
                if (distanceSymbol < 0 || distanceSymbol >= DistanceBase.Length)
                {
                    throw Corrupt("invalid distance code");
                }

                var distance = DistanceBase[distanceSymbol] + Bits(DistanceExtra[distanceSymbol]);
                if (distance > outputLength)
                {
                    throw Corrupt("invalid distance too far back");
                }

                Reserve(length);
                for (var index = 0; index < length; index++)
                {
                    output[outputLength] = output[outputLength - distance];
                    outputLength++;
                }
            }
        }

        private void Reserve(int extra)
        {
            if (outputLength + extra > output.Length)
            {
                Array.Resize(ref output, Math.Max(output.Length * 2, outputLength + extra));
            }
        }

        private uint Adler32()
        {
            uint a = 1;
            uint b = 0;
            var offset = 0;
            while (offset < outputLength)
            {
                var end = Math.Min(outputLength, offset + 5552);
                for (; offset < end; offset++)
                {
                    a += output[offset];
                    b += a;
                }

                a %= 65521;
                b %= 65521;
            }

            return (b << 16) | a;
        }
    }
}
