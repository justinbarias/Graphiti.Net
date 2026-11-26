namespace Port.Evaluation.JudgeHost;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>
/// Compares the C# codec with the Python codec. port/tools/record_codec_values.py ran
/// port/judge/host-python/codec.py on generated values and wrote <c>json.dumps(encode(value), ensure_ascii=True)</c>.
/// The C# side decodes the same value from its argument tags, encodes it, and writes the line.
/// </summary>
public sealed class PythonCodecTests(ITestOutputHelper output)
{
    [Fact]
    public void EachRecordedValueEncodesAsPythonWritesIt()
    {
        var failures = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Repository.Path("tests", "Port.Evaluation", "JudgeHost", "python-codec-values.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var kind = document.RootElement.GetProperty("kind").GetString()!;
            var expected = document.RootElement.GetProperty("output").GetString()!;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            var decoder = new TestDecoder();
            var actual = JudgeJson.Dumps(Codec.Encode(decoder.Decode(document.RootElement.GetProperty("input")), decoder.Registry));
            if (actual != expected)
            {
                failures.Add($"{kind}\n  python: {expected}\n  dotnet: {actual}");
            }
        }

        output.WriteLine(string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.True(failures.Count == 0, $"{failures.Count} differ:\n" + string.Join("\n", failures.Take(15)));
        Assert.True(counts.Values.Sum() > 3000);
    }

    // Not theory data: an attribute argument with a lone surrogate is stored as UTF-8 and loses it.
    [Fact]
    public void LoneSurrogatesAndControlsAreEscapedAsPythonEscapesThem()
    {
        (string Text, string Json)[] rows =
        [
            ("\ud800", "\"\\ud800\""),
            ("\udfff", "\"\\udfff\""),
            ("a\ud83d\ude00b", "\"a\\ud83d\\ude00b\""),
            ("\udc00\ud800", "\"\\udc00\\ud800\""),
            ("\u2028\u007f~ ", "\"\\u2028\\u007f~ \""),
            ("\"\\/\b\f\n\r\t\u0001", "\"\\\"\\\\/\\b\\f\\n\\r\\t\\u0001\""),
        ];
        foreach (var (text, json) in rows)
        {
            Assert.Equal(json, JudgeJson.Dumps(Codec.Encode(text, new JudgeRegistry())));

            // And the decoder reads the escape back to the same UTF-16 units.
            Assert.Equal(text, new TestDecoder().DecodeText(json));
        }
    }

    [Fact]
    public void ALoneSurrogateInAKeyIsKept()
    {
        var decoded = new TestDecoder().DecodeText("{\"\\ud800\": 1}");
        var dict = Assert.IsType<OrderedDictionary<string, object?>>(decoded);
        Assert.Equal("\ud800", Assert.Single(dict).Key);
        Assert.Equal("{\"\\ud800\": 1}", JudgeJson.Dumps(Codec.Encode(dict, new JudgeRegistry())));
    }

    [Fact]
    public void FloatsAndIntsAreWrittenAsPythonWritesThem()
    {
        var registry = new JudgeRegistry();
        Assert.Equal("1.0", JudgeJson.Dumps(Codec.Encode(1.0, registry)));
        Assert.Equal("-0.0", JudgeJson.Dumps(Codec.Encode(-0.0, registry)));
        Assert.Equal("1e+16", JudgeJson.Dumps(Codec.Encode(1e16, registry)));
        Assert.Equal("1e-05", JudgeJson.Dumps(Codec.Encode(1e-5, registry)));
        Assert.Equal("{\"$float\": \"nan\"}", JudgeJson.Dumps(Codec.Encode(double.NaN, registry)));
        Assert.Equal("{\"$float\": \"-inf\"}", JudgeJson.Dumps(Codec.Encode(double.NegativeInfinity, registry)));
        Assert.Equal("123456789012345678901234567890", JudgeJson.Dumps(Codec.Encode(System.Numerics.BigInteger.Parse("123456789012345678901234567890", System.Globalization.CultureInfo.InvariantCulture), registry)));
        Assert.Equal("[]", JudgeJson.Dumps(Codec.Encode(new List<object?>(), registry)));
        Assert.Equal("{}", JudgeJson.Dumps(Codec.Encode(new Graphiti.PyCompat.PyDict(), registry)));
    }

    [Fact]
    public void ARegisteredTypeEncodesItselfWhenNested()
    {
        var registry = new JudgeRegistry().AddType<Inner>("Inner", i => [new("value", i.Value)]);
        var outer = new TestModel("Outer", new OrderedDictionary<string, object?> { ["inner"] = new Inner(new List<object?> { 1L }) });
        Assert.Equal(
            "{\"$type\": \"Outer\", \"inner\": {\"$type\": \"Inner\", \"value\": [1]}}",
            JudgeJson.Dumps(Codec.Encode(outer, registry)));
    }

    [Fact]
    public void AnUnregisteredPathIsNotBound()
    {
        var decoder = new ValueDecoder(new JudgeRegistry());
        foreach (var json in new[] { "{\"$ref\": \"m:x\"}", "{\"$enum\": \"m:E\", \"value\": 1}", "{\"$model\": \"m:M\", \"data\": {}}", "{\"$step\": [0, \"\"]}", "{\"$graphiti\": \"driver\"}", "{\"$embed\": \"text\"}" })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Throws<JudgeNotBoundException>(() => decoder.Decode(document.RootElement));
        }
    }

    private sealed record Inner(object? Value);
}
