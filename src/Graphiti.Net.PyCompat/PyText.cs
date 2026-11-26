namespace Graphiti.PyCompat;

using System.Text;

/// <summary>
/// The <c>str</c> methods of Python 3.12. Python counts code points and holds a lone surrogate as
/// one character. A .NET string cannot tell a lone surrogate pair from one code point, so a high
/// surrogate directly before a low surrogate is always one code point here. An operation that puts
/// a low surrogate next to an earlier high one, for example a reverse slice of two lone surrogates,
/// joins them. Python keeps two code points. A .NET string cannot hold that difference (debt T24).
/// </summary>
internal static class PyText
{
    /// <summary><c>Py_UNICODE_ISSPACE</c>: the whitespace set of <c>str.split()</c> and <c>str.strip()</c>.</summary>
    internal static bool IsSpaceCodePoint(int c) =>
        c is (>= 0x09 and <= 0x0D) or (>= 0x1C and <= 0x20) or 0x85 or 0xA0 or 0x1680
            or (>= 0x2000 and <= 0x200A) or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000;

    /// <summary><c>not chr(c).isprintable()</c>: the code points that <c>repr</c> escapes.</summary>
    internal static bool IsNonPrintable(int cp) => InRanges(PyUnicodeData.NonPrintable, cp);

    /// <summary>The code points of a string. A lone surrogate is one code point.</summary>
    internal static int[] ToCodePoints(string s)
    {
        var result = new List<int>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                result.Add(char.ConvertToUtf32(c, s[i + 1]));
                i++;
            }
            else
            {
                result.Add(c);
            }
        }

        return [.. result];
    }

    /// <summary>The string of the code points in a range. A surrogate value stays as a lone surrogate.</summary>
    internal static string FromCodePoints(ReadOnlySpan<int> codePoints)
    {
        var sb = new StringBuilder(codePoints.Length);
        foreach (var cp in codePoints)
        {
            AppendCodePoint(sb, cp);
        }

        return sb.ToString();
    }

    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp < 0x10000)
        {
            sb.Append((char)cp);
        }
        else
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }
    }

    /// <summary><c>for ch in s</c>: each item is a string that holds one code point. A lone surrogate stays.</summary>
    internal static IEnumerable<string> CodePoints(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                yield return s.Substring(i, 2);
                i++;
            }
            else
            {
                yield return s[i].ToString();
            }
        }
    }

    /// <summary><c>ord(c)</c>. Throws <see cref="InvalidCastException"/> (<c>TypeError</c>) unless the string is one code point.</summary>
    internal static long Ord(string c)
    {
        var codePoints = ToCodePoints(c);
        if (codePoints.Length != 1)
        {
            throw new InvalidCastException(
                $"ord() expected a character, but string of length {codePoints.Length} found");
        }

        return codePoints[0];
    }

    // ---- case ----

    private static bool InRanges(int[] ranges, int cp)
    {
        var lo = 0;
        var hi = (ranges.Length / 2) - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cp < ranges[2 * mid])
            {
                hi = mid - 1;
            }
            else if (cp > ranges[(2 * mid) + 1])
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static string? Lookup(int[] keys, string[] values, int cp)
    {
        var index = Array.BinarySearch(keys, cp);
        return index >= 0 ? values[index] : null;
    }

    /// <summary>The Final_Sigma test of CPython: is the capital sigma at <paramref name="i"/> the last letter of a word.</summary>
    private static bool IsFinalSigma(int[] cps, int i)
    {
        var j = i - 1;
        while (j >= 0 && InRanges(PyUnicodeData.CaseIgnorable, cps[j]))
        {
            j--;
        }

        if (j < 0 || !InRanges(PyUnicodeData.Cased, cps[j]))
        {
            return false;
        }

        j = i + 1;
        while (j < cps.Length && InRanges(PyUnicodeData.CaseIgnorable, cps[j]))
        {
            j++;
        }

        return j == cps.Length || !InRanges(PyUnicodeData.Cased, cps[j]);
    }

    private static void AppendLower(StringBuilder sb, int[] cps, int i)
    {
        var cp = cps[i];
        if (cp == 0x3A3)
        {
            sb.Append(IsFinalSigma(cps, i) ? 'ς' : 'σ');
            return;
        }

        var mapped = Lookup(PyUnicodeData.LowercaseKeys, PyUnicodeData.LowercaseValues, cp);
        if (mapped is null)
        {
            AppendCodePoint(sb, cp);
        }
        else
        {
            sb.Append(mapped);
        }
    }

    /// <summary><c>s.lower()</c>: the full Unicode lowercase mapping, with the final sigma rule.</summary>
    internal static string Lower(string s)
    {
        var cps = ToCodePoints(s);
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < cps.Length; i++)
        {
            AppendLower(sb, cps, i);
        }

        return sb.ToString();
    }

    /// <summary><c>s.capitalize()</c>: the first code point in titlecase, the rest in lowercase.</summary>
    internal static string Capitalize(string s)
    {
        var cps = ToCodePoints(s);
        if (cps.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length);
        var title = Lookup(PyUnicodeData.TitlecaseKeys, PyUnicodeData.TitlecaseValues, cps[0]);
        if (title is null)
        {
            AppendCodePoint(sb, cps[0]);
        }
        else
        {
            sb.Append(title);
        }

        for (var i = 1; i < cps.Length; i++)
        {
            AppendLower(sb, cps, i);
        }

        return sb.ToString();
    }

    /// <summary><c>s.isupper()</c>: no lowercase or titlecase code point, and at least one uppercase one.</summary>
    internal static bool IsUpper(string s)
    {
        var cased = false;
        foreach (var cp in ToCodePoints(s))
        {
            if (InRanges(PyUnicodeData.Lower, cp) || InRanges(PyUnicodeData.Title, cp))
            {
                return false;
            }

            if (InRanges(PyUnicodeData.Upper, cp))
            {
                cased = true;
            }
        }

        return cased;
    }

    // ---- strip, split, find ----

    private static string Cut(int[] cps, int start, int end) =>
        FromCodePoints(cps.AsSpan(start, end - start));

    private static string StripCore(string s, Func<int, bool> remove, bool left, bool right)
    {
        var cps = ToCodePoints(s);
        var start = 0;
        var end = cps.Length;
        if (left)
        {
            while (start < end && remove(cps[start]))
            {
                start++;
            }
        }

        if (right)
        {
            while (end > start && remove(cps[end - 1]))
            {
                end--;
            }
        }

        return Cut(cps, start, end);
    }

    /// <summary><c>s.strip()</c> with Python's whitespace set.</summary>
    internal static string Strip(string s) => StripCore(s, IsSpaceCodePoint, true, true);

    /// <summary><c>s.strip(chars)</c>: removes each code point that is in <paramref name="chars"/>.</summary>
    internal static string Strip(string s, string chars)
    {
        var set = ToCodePoints(chars).ToHashSet();
        return StripCore(s, set.Contains, true, true);
    }

    /// <summary><c>s.lstrip()</c> with Python's whitespace set.</summary>
    internal static string LStrip(string s) => StripCore(s, IsSpaceCodePoint, true, false);

    /// <summary><c>s.rstrip()</c> with Python's whitespace set.</summary>
    internal static string RStrip(string s) => StripCore(s, IsSpaceCodePoint, false, true);

    /// <summary><c>s.rstrip(chars)</c>: removes each code point that is in <paramref name="chars"/>.</summary>
    internal static string RStrip(string s, string chars)
    {
        var set = ToCodePoints(chars).ToHashSet();
        return StripCore(s, set.Contains, false, true);
    }

    /// <summary><c>s.split()</c>: splits on runs of whitespace and returns no empty item.</summary>
    internal static List<string> Split(string s)
    {
        var cps = ToCodePoints(s);
        var result = new List<string>();
        var i = 0;
        while (i < cps.Length)
        {
            while (i < cps.Length && IsSpaceCodePoint(cps[i]))
            {
                i++;
            }

            if (i == cps.Length)
            {
                break;
            }

            var start = i;
            while (i < cps.Length && !IsSpaceCodePoint(cps[i]))
            {
                i++;
            }

            result.Add(Cut(cps, start, i));
        }

        return result;
    }

    /// <summary>
    /// <c>s.split(sep, maxsplit)</c>. <paramref name="maxsplit"/> is the number of cuts, and a negative
    /// value means all. An empty separator throws <see cref="ArgumentException"/> (<c>ValueError</c>).
    /// </summary>
    internal static List<string> Split(string s, string sep, long maxsplit = -1)
    {
        if (sep.Length == 0)
        {
            throw new ArgumentException("empty separator");
        }

        var cps = ToCodePoints(s);
        var sepCps = ToCodePoints(sep);
        var result = new List<string>();
        var start = 0;
        var i = 0;
        while ((maxsplit < 0 || result.Count < maxsplit) && i <= cps.Length - sepCps.Length)
        {
            if (cps.AsSpan(i, sepCps.Length).SequenceEqual(sepCps))
            {
                result.Add(Cut(cps, start, i));
                i += sepCps.Length;
                start = i;
            }
            else
            {
                i++;
            }
        }

        result.Add(Cut(cps, start, cps.Length));
        return result;
    }

    /// <summary><c>s.splitlines()</c>: breaks at the line boundaries of Python, and keeps no line end.</summary>
    internal static List<string> SplitLines(string s)
    {
        var cps = ToCodePoints(s);
        var result = new List<string>();
        var start = 0;
        var i = 0;
        while (i < cps.Length)
        {
            if (cps[i] is (>= 0x0A and <= 0x0D) or (>= 0x1C and <= 0x1E) or 0x85 or 0x2028 or 0x2029)
            {
                var end = i;
                i += cps[i] == 0x0D && i + 1 < cps.Length && cps[i + 1] == 0x0A ? 2 : 1;
                result.Add(Cut(cps, start, end));
                start = i;
            }
            else
            {
                i++;
            }
        }

        if (start < cps.Length)
        {
            result.Add(Cut(cps, start, cps.Length));
        }

        return result;
    }

    private static (int Start, int End) AdjustIndices(int length, long? start, long? end)
    {
        long Fix(long? value, long defaultValue)
        {
            if (value is null)
            {
                return defaultValue;
            }

            var v = value.Value;
            if (v < 0)
            {
                v += length;
                return v < 0 ? 0 : v;
            }

            return Math.Min(v, length);
        }

        var s = Fix(start, 0);
        var e = Fix(end, length);
        return ((int)s, (int)e);
    }

    private static int FindCore(string s, string sub, long? start, long? end, bool last)
    {
        var cps = ToCodePoints(s);
        var subCps = ToCodePoints(sub);
        var (from, to) = AdjustIndices(cps.Length, start, end);
        if (start is > 0 && start.Value > cps.Length)
        {
            return -1;
        }

        if (to - from < subCps.Length)
        {
            return -1;
        }

        var lastStart = to - subCps.Length;
        for (var k = last ? lastStart : from; last ? k >= from : k <= lastStart; k += last ? -1 : 1)
        {
            if (cps.AsSpan(k, subCps.Length).SequenceEqual(subCps))
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary><c>s.find(sub, start, end)</c>. Positions are code points. Returns -1 if there is no match.</summary>
    internal static long Find(string s, string sub, long? start = null, long? end = null) =>
        FindCore(s, sub, start, end, false);

    /// <summary><c>s.rfind(sub, start, end)</c>. Positions are code points. Returns -1 if there is no match.</summary>
    internal static long RFind(string s, string sub, long? start = null, long? end = null) =>
        FindCore(s, sub, start, end, true);

    // ---- UTF-8 ----

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// <c>s.encode('utf-8')</c>. A lone surrogate throws <see cref="EncoderFallbackException"/>
    /// (<c>UnicodeEncodeError</c>).
    /// </summary>
    internal static byte[] EncodeUtf8(string s) => StrictUtf8.GetBytes(s);

    /// <summary><c>s.encode('utf-8', errors='ignore').decode('utf-8')</c>: removes each lone surrogate.</summary>
    internal static string DropLoneSurrogates(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var cp in ToCodePoints(s))
        {
            if (cp is < 0xD800 or > 0xDFFF)
            {
                AppendCodePoint(sb, cp);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>b.decode('utf-8', errors)</c>. <paramref name="errors"/> is <c>strict</c> or <c>ignore</c>.
    /// Strict mode throws <see cref="DecoderFallbackException"/> (<c>UnicodeDecodeError</c>).
    /// A malformed sequence is the longest prefix that can still start a valid sequence, as in CPython.
    /// The message has CPython's reason (<c>invalid start byte</c>, <c>invalid continuation byte</c>, or
    /// <c>unexpected end of data</c>) and its byte or position range.
    /// </summary>
    internal static string DecodeUtf8(ReadOnlySpan<byte> bytes, string errors = "strict")
    {
        if (errors is not ("strict" or "ignore"))
        {
            throw new ArgumentException($"unknown error handler name '{errors}'");
        }

        var sb = new StringBuilder(bytes.Length);
        var i = 0;
        while (i < bytes.Length)
        {
            var b0 = bytes[i];
            if (b0 < 0x80)
            {
                sb.Append((char)b0);
                i++;
                continue;
            }

            int need;
            int min2 = 0x80;
            int max2 = 0xBF;
            int cp;
            if (b0 is >= 0xC2 and <= 0xDF)
            {
                need = 1;
                cp = b0 & 0x1F;
            }
            else if (b0 is >= 0xE0 and <= 0xEF)
            {
                need = 2;
                cp = b0 & 0x0F;
                if (b0 == 0xE0)
                {
                    min2 = 0xA0;
                }
                else if (b0 == 0xED)
                {
                    max2 = 0x9F;
                }
            }
            else if (b0 is >= 0xF0 and <= 0xF4)
            {
                need = 3;
                cp = b0 & 0x07;
                if (b0 == 0xF0)
                {
                    min2 = 0x90;
                }
                else if (b0 == 0xF4)
                {
                    max2 = 0x8F;
                }
            }
            else
            {
                need = 0;
                cp = 0;
            }

            var valid = need > 0 ? 1 : 0;
            var truncated = false;
            if (need > 0)
            {
                for (var k = 1; k <= need; k++)
                {
                    if (i + k >= bytes.Length)
                    {
                        truncated = true;
                        break;
                    }

                    var b = bytes[i + k];
                    var lo = k == 1 ? min2 : 0x80;
                    var hi = k == 1 ? max2 : 0xBF;
                    if (b < lo || b > hi)
                    {
                        break;
                    }

                    cp = (cp << 6) | (b & 0x3F);
                    valid++;
                }
            }

            if (need > 0 && valid == need + 1)
            {
                AppendCodePoint(sb, cp);
                i += valid;
                continue;
            }

            if (errors == "strict")
            {
                var length = Math.Max(valid, 1);
                var reason = need == 0 ? "invalid start byte" : truncated ? "unexpected end of data" : "invalid continuation byte";
                var where = length == 1
                    ? $"byte 0x{b0:x2} in position {i}"
                    : $"bytes in position {i}-{i + length - 1}";
                throw new DecoderFallbackException($"'utf-8' codec can't decode {where}: {reason}");
            }

            i += Math.Max(valid, 1);
        }

        return sb.ToString();
    }

    // ---- replace and count ----

    /// <summary>
    /// <c>s.replace(old, new, count)</c>. A <paramref name="count"/> below zero replaces each match, and zero replaces none.
    /// The matches do not overlap and go from left to right. An empty <paramref name="old"/> matches before each code point
    /// and at the end. The match works on code points, so a lone surrogate never matches half of an emoji.
    /// </summary>
    internal static string Replace(string s, string old, string @new, long count = -1)
    {
        var cps = ToCodePoints(s);
        var oldCps = ToCodePoints(old);
        var sb = new StringBuilder(s.Length);
        var replaced = 0L;
        var i = 0;
        if (oldCps.Length == 0)
        {
            for (; i <= cps.Length; i++)
            {
                if (count < 0 || replaced < count)
                {
                    sb.Append(@new);
                    replaced++;
                }

                if (i < cps.Length)
                {
                    AppendCodePoint(sb, cps[i]);
                }
            }

            return sb.ToString();
        }

        while (i < cps.Length)
        {
            if ((count < 0 || replaced < count)
                && i <= cps.Length - oldCps.Length
                && cps.AsSpan(i, oldCps.Length).SequenceEqual(oldCps))
            {
                sb.Append(@new);
                replaced++;
                i += oldCps.Length;
            }
            else
            {
                AppendCodePoint(sb, cps[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>s.count(sub)</c>: the number of matches of <paramref name="sub"/> that do not overlap, from left to right.
    /// An empty <paramref name="sub"/> gives the number of code points plus one.
    /// </summary>
    internal static long Count(string s, string sub)
    {
        var cps = ToCodePoints(s);
        var subCps = ToCodePoints(sub);
        if (subCps.Length == 0)
        {
            return cps.Length + 1L;
        }

        var found = 0L;
        var i = 0;
        while (i <= cps.Length - subCps.Length)
        {
            if (cps.AsSpan(i, subCps.Length).SequenceEqual(subCps))
            {
                found++;
                i += subCps.Length;
            }
            else
            {
                i++;
            }
        }

        return found;
    }
}
