namespace Port.Evaluation.JudgeHost;

using System.Numerics;
using System.Text.Json;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// Decoding that starts from the JSON text of a case, as the host sees it, and asserts the runtime type of each
/// value (RULES.md R6). A test that builds the value by hand cannot see a wrong runtime type.
/// </summary>
public sealed class DecodeFromJsonTests
{
    [Theory]
    [InlineData("1", typeof(long))]
    [InlineData("-9223372036854775808", typeof(long))]
    [InlineData("9223372036854775807", typeof(long))]
    [InlineData("9223372036854775808", typeof(BigInteger))]
    [InlineData("-9223372036854775809", typeof(BigInteger))]
    [InlineData("1.0", typeof(double))]
    [InlineData("1e5", typeof(double))]
    [InlineData("\"a\"", typeof(string))]
    [InlineData("true", typeof(bool))]
    [InlineData("{\"$tuple\": [1]}", typeof(PyTuple))]
    [InlineData("{\"$set\": [1]}", typeof(PySet<object?>))]
    [InlineData("{\"$dict\": [[1, 2]]}", typeof(PyDict))]
    [InlineData("{\"$dict\": [[\"a\", 2]]}", typeof(OrderedDictionary<string, object?>))]
    [InlineData("{\"$dt\": \"2026-03-01T00:00:00+00:00\"}", typeof(PyDateTime))]
    [InlineData("{\"$bytes\": \"00ff\"}", typeof(byte[]))]
    public void AJsonValueDecodesToItsValueModelType(string json, Type type)
    {
        Assert.IsType(type, Decode(json));
    }

    [Fact]
    public void AnIntInsideAListOrADictIsALong()
    {
        var list = Assert.IsType<List<object?>>(Decode("[1, [2], {\"a\": 3}]"));
        Assert.IsType<long>(list[0]);
        Assert.IsType<long>(Assert.IsType<List<object?>>(list[1])[0]);
        Assert.IsType<long>(Assert.IsType<OrderedDictionary<string, object?>>(list[2])["a"]);
        var tuple = Assert.IsType<PyTuple>(Decode("{\"$tuple\": [1, 2]}"));
        Assert.IsType<long>(tuple[0]);
        var dict = Assert.IsType<PyDict>(Decode("{\"$dict\": [[1, 2]]}"));
        Assert.IsType<long>(Assert.Single(dict).Key);
        Assert.IsType<long>(Assert.Single(dict).Value);
    }

    [Fact]
    public void ATypedReadStartsFromJsonText()
    {
        var p = Call("[1, null, [1, 2], {\"a\": 1}, 2]").Signature("a", "b", "c", "d", "e");
        Assert.Equal(1L, p.Long("a"));
        Assert.Null(p.NullableLong("b"));
        Assert.Equal(2L, p.NullableLong("e"));
        Assert.Equal([1L, 2L], p.List<long>("c"));
        Assert.Equal(1L, p.Dict<long>("d")["a"]);
        Assert.Equal(2.0, p.Double("e"));
    }

    [Fact]
    public void AKeywordArgumentStartsFromJsonText()
    {
        var p = Call("[]", "{\"limit\": 10, \"labels\": [\"A\"]}").Signature("limit", "labels");
        Assert.Equal(10L, p.Long("limit"));
        Assert.Equal(["A"], p.List<string>("labels"));
    }

    [Fact]
    public void ATupleIsNotAList()
    {
        // Mutant M12: a $tuple that decodes as a list would pass for a list parameter.
        var p = Call("[{\"$tuple\": [\"a\"]}]").Signature("xs");
        Assert.Throws<JudgeTypeRejectedException>(() => p.List<string>("xs"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.Get<IEnumerable<string>>("xs"));
        Assert.IsType<PyTuple>(p.Get<PyTuple>("xs"));
    }

    [Fact]
    public void ADateTimeReplacesEachZAsPythonDoes()
    {
        // Mutant M13. fromisoformat takes any one character between the date and the time, so without the
        // replacement 'Z' is a separator. With it, the text is not valid, as in Python.
        Assert.Throws<ArgumentException>(() => Decode("{\"$dt\": \"2026-03-01Z00:00:00\"}"));
        var utc = Assert.IsType<PyDateTime>(Decode("{\"$dt\": \"2026-03-01T00:00:00Z\"}"));
        Assert.Equal("2026-03-01T00:00:00+00:00", utc.IsoFormat());
    }

    [Fact]
    public void TheLastValueOfARepeatedKeyWins()
    {
        // Mutant M14. json.loads keeps the last value, for a tag and for a plain key.
        Assert.True(double.IsPositiveInfinity(Assert.IsType<double>(Decode("{\"$float\": \"nan\", \"$float\": \"inf\"}"))));
        var dict = Assert.IsType<OrderedDictionary<string, object?>>(Decode("{\"a\": 1, \"b\": 2, \"a\": 3}"));
        Assert.Equal(["a", "b"], dict.Keys);
        Assert.Equal(3L, dict["a"]);
    }

    [Theory]
    [InlineData(int.MinValue, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData((long)int.MinValue - 1, false)]
    [InlineData((long)int.MaxValue + 1, false)]
    public void AnIntParameterTakesOnlyAnIntThatFits(long value, bool accepted)
    {
        // Mutant M08: the boundaries of a C# int.
        var p = Call($"[{value}]").Signature("n");
        if (accepted)
        {
            Assert.Equal(value, p.Get<int>("n"));
        }
        else
        {
            Assert.Throws<JudgeTypeRejectedException>(() => p.Get<int>("n"));
        }
    }

    [Fact]
    public void ASequenceOrASetParameterReadsFromJsonText()
    {
        var p = Call("[[\"a\", \"b\"], {\"$set\": [1, 2]}, [1, 2], {\"$set\": [\"x\"]}]").Signature("seq", "set", "list", "hash");
        Assert.Equal(["a", "b"], p.Get<IEnumerable<string>>("seq"));
        Assert.Equal(["a", "b"], p.Get<IReadOnlyList<string>>("seq"));
        Assert.Equal([1L, 2L], p.Get<PySet<long>>("set"));
        Assert.Equal(["x"], p.Get<HashSet<string>>("hash"));
        Assert.IsType<PySet<object?>>(p.Get<PySet<object?>>("set"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.Get<PySet<long>>("list"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.Get<HashSet<long>>("hash"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.Get<IEnumerable<long>>("set"));
    }

    private static object? Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ValueDecoder(new JudgeRegistry()).Decode(document.RootElement);
    }

    private static JudgeArguments Call(string args, string kwargs = "{}")
    {
        using var document = JsonDocument.Parse($"{{\"fn\": \"f\", \"args\": {args}, \"kwargs\": {kwargs}}}");
        return JudgeArguments.Decode(document.RootElement, new ValueDecoder(new JudgeRegistry()));
    }
}
