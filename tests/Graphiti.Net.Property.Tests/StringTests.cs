namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PythonValues;

public class StringTests
{
    [Fact]
    public void Len_matches_Python() => CheckLong("len", c => Py.Len(Str(c, "s")));

    [Fact]
    public void CodePoints_match_Python() =>
        Check("code_points", c => PyText.CodePoints(Str(c, "s")).Select(x => PyText.ToCodePoints(x).Single()).ToArray(),
            (c, v) => Assert.Equal(Ints(c.GetProperty("r")), v));

    [Fact]
    public void At_matches_Python() => CheckText("at", c => Py.At(Str(c, "s"), c.GetProperty("i").GetInt64()));

    [Fact]
    public void Slice_of_a_string_matches_Python() => CheckText("slice_str", c => Py.Slice(Str(c, "s"), OptLong(c, "a"), OptLong(c, "b"), OptLong(c, "step")));

    [Fact]
    public void Slice_of_a_list_matches_Python() =>
        Check("slice_list",
            c => Py.Slice(Ints(c.GetProperty("xs")).ToList(), OptLong(c, "a"), OptLong(c, "b"), OptLong(c, "step")).ToArray(),
            (c, v) => Assert.Equal(Ints(c.GetProperty("r")), v));

    [Fact]
    public void CompareStrings_matches_Python() => CheckInt("compare_strings", c => Py.CompareStrings(Text(c, "a"), Text(c, "b")));

    [Fact]
    public void ReprStr_matches_Python() => CheckText("repr_str", c => Py.ReprStr(Str(c, "s")));

    [Fact]
    public void Lower_matches_Python() => CheckText("lower", c => PyText.Lower(Str(c, "s")));

    [Fact]
    public void Capitalize_matches_Python() => CheckText("capitalize", c => PyText.Capitalize(Str(c, "s")));

    [Fact]
    public void IsUpper_matches_Python() => CheckBool("is_upper", c => PyText.IsUpper(Str(c, "s")));

    [Fact]
    public void Strip_matches_Python() => CheckText("strip", c => PyText.Strip(Str(c, "s")));

    [Fact]
    public void LStrip_matches_Python() => CheckText("lstrip", c => PyText.LStrip(Str(c, "s")));

    [Fact]
    public void RStrip_matches_Python() => CheckText("rstrip", c => PyText.RStrip(Str(c, "s")));

    [Fact]
    public void Strip_with_chars_matches_Python() => CheckText("strip_chars", c => PyText.Strip(Str(c, "s"), Text(c, "chars")));

    [Fact]
    public void RStrip_with_chars_matches_Python() => CheckText("rstrip_chars", c => PyText.RStrip(Str(c, "s"), Text(c, "chars")));

    [Fact]
    public void Split_on_whitespace_matches_Python() => CheckTexts("split_ws", c => PyText.Split(Str(c, "s")));

    [Fact]
    public void Split_on_a_separator_matches_Python() => CheckTexts("split_sep", c => PyText.Split(Str(c, "s"), Text(c, "sep"), c.GetProperty("n").GetInt64()));

    [Fact]
    public void SplitLines_matches_Python() => CheckTexts("splitlines", c => PyText.SplitLines(Str(c, "s")));

    [Fact]
    public void Find_matches_Python() => CheckLong("find", c => PyText.Find(Str(c, "s"), Text(c, "sub"), OptLong(c, "a"), OptLong(c, "b")));

    [Fact]
    public void RFind_matches_Python() => CheckLong("rfind", c => PyText.RFind(Str(c, "s"), Text(c, "sub"), OptLong(c, "a"), OptLong(c, "b")));

    [Fact]
    public void Ord_matches_Python() => CheckLong("ord", c => PyText.Ord(Str(c, "s")));

    [Fact]
    public void EncodeUtf8_matches_Python() =>
        Check("encode_utf8", c => PyText.EncodeUtf8(Str(c, "s")),
            (c, v) => Assert.Equal(Ints(c.GetProperty("r")).Select(x => (byte)x).ToArray(), v));

    [Fact]
    public void DecodeUtf8_matches_Python() =>
        CheckText("decode_utf8", c => PyText.DecodeUtf8(
            Ints(c.GetProperty("b")).Select(x => (byte)x).ToArray(), c.GetProperty("errors").GetString()!));

    [Fact]
    public void DropLoneSurrogates_matches_Python() => CheckText("drop_lone_surrogates", c => PyText.DropLoneSurrogates(Str(c, "s")));

    [Fact]
    public void Single_code_point_tables_match_Python() => Check("code_point_props",
        c => PyText.FromCodePoints([c.GetProperty("c").GetInt32()]),
        (c, s) =>
        {
            Assert.Equal(c.GetProperty("space").GetBoolean(), PyText.IsSpaceCodePoint(c.GetProperty("c").GetInt32()));
            Assert.Equal(c.GetProperty("upper").GetBoolean(), PyText.IsUpper(s));
            Assert.Equal(c.GetProperty("a_upper").GetBoolean(), PyText.IsUpper("A" + s));
            Assert.Equal(c.GetProperty("printable").GetBoolean(), !PyText.IsNonPrintable(c.GetProperty("c").GetInt32()));
            if (c.GetProperty("digit") is { ValueKind: JsonValueKind.Number } digit)
            {
                Assert.Equal(digit.GetInt64(), Py.Int(s));
            }
            else
            {
                Assert.Throws<ArgumentException>(() => Py.Int(s));
            }

            Assert.Equal(Ints(c.GetProperty("lower_map")), PyText.ToCodePoints(PyText.Lower(s)));
            Assert.Equal(Ints(c.GetProperty("title_map")), PyText.ToCodePoints(PyText.Capitalize(s)));
        });

    // Laws that hold for any input. They do not use the Python values.

    private static readonly Gen<int> CodePoint = Gen.OneOf(
        Gen.Int[0x20, 0x7E], Gen.Int[0x80, 0x17F], Gen.Int[0x300, 0x36F], Gen.Int[0x4E00, 0x4E20],
        Gen.Int[0x1F600, 0x1F64F], Gen.Const(0x20), Gen.Const(0x2028), Gen.Const(0x85));

    private static readonly Gen<string> Text8 =
        CodePoint.List[0, 12].Select(l => PyText.FromCodePoints([.. l]));

    [Fact]
    public void Len_counts_the_items_of_CodePoints() =>
        Text8.Sample(s => Py.Len(s) == PyText.CodePoints(s).Count());

    [Fact]
    public void Full_slice_with_step_minus_one_reverses_code_points() =>
        Text8.Sample(s => Py.Slice(s, null, null, -1) == PyText.FromCodePoints([.. PyText.ToCodePoints(s).Reverse()]));

    [Fact]
    public void Slice_of_the_whole_string_is_the_string() =>
        Text8.Sample(s => Py.Slice(s, null, null) == s);

    [Fact]
    public void Join_undoes_Split_on_a_separator() =>
        Gen.Select(Text8, Gen.OneOf(",", "ab", "\U0001F600", " ")).Sample((s, sep) =>
            string.Join(sep, PyText.Split(s, sep)) == s);

    [Fact]
    public void Split_with_maxsplit_cuts_at_most_that_many_times() =>
        Gen.Select(Text8, Gen.Int[0, 4]).Sample((s, n) => PyText.Split(s, " ", n).Count <= n + 1);

    [Fact]
    public void Utf8_round_trips_a_string_without_lone_surrogates() =>
        Text8.Sample(s => PyText.DecodeUtf8(PyText.EncodeUtf8(s)) == s);

    [Fact]
    public void Strip_leaves_no_whitespace_at_either_end() =>
        Text8.Sample(s =>
        {
            var t = PyText.Strip(s);
            var cps = PyText.ToCodePoints(t);
            return cps.Length == 0 || (!PyText.IsSpaceCodePoint(cps[0]) && !PyText.IsSpaceCodePoint(cps[^1]));
        });

    [Fact]
    public void CompareStrings_is_zero_only_for_equal_strings_and_is_antisymmetric() =>
        Gen.Select(Text8, Text8).Sample((a, b) =>
            (Py.CompareStrings(a, b) == 0) == (a == b) && Py.CompareStrings(a, b) == -Py.CompareStrings(b, a));

    [Fact]
    public void Find_result_is_where_the_slice_matches() =>
        Gen.Select(Text8, Gen.OneOf("a", "b", " ", "\U0001F600", "e")).Sample((s, sub) =>
        {
            var i = PyText.Find(s, sub);
            return i < 0 ? !s.Contains(sub, StringComparison.Ordinal) : Py.Slice(s, i, i + Py.Len(sub)) == sub;
        });
}
