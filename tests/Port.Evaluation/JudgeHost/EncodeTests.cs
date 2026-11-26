namespace Port.Evaluation.JudgeHost;

using Graphiti.JudgeHost.Core;

/// <summary>Encodings of C# types that no Python value records: tuples, vectors, and built-in classes.</summary>
public sealed class EncodeTests
{
    [Fact]
    public void AValueTupleIsAList()
    {
        Assert.Equal("[1, \"a\"]", Dumps((1L, "a")));
        Assert.Equal("[1, 2.5, null]", Dumps((1L, 2.5, (string?)null)));
        Assert.Equal("[1, 2, 3, 4, 5, 6, 7, 8]", Dumps((1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L)));
        Assert.Equal("[[1, [true, \"x\"]], []]", Dumps(((1L, (true, "x")), new List<object?>())));
        Assert.Equal("[[1, 2], {\"k\": [3, 4]}]", Dumps(new List<object?> { (1L, 2L), new OrderedDictionary<string, object?> { ["k"] = (3L, 4L) } }));
    }

    [Fact]
    public void ASystemTupleIsAList()
    {
        Assert.Equal("[1, \"a\"]", Dumps(Tuple.Create(1L, "a")));
        Assert.Equal("[1, 2, 3]", Dumps(Tuple.Create(1L, 2L, 3L)));
        Assert.Equal("[1, 2, 3, 4, 5, 6, 7, 8]", Dumps(Tuple.Create(1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L)));
    }

    [Fact]
    public void AVectorWritesNanAndInfinityAsTags()
    {
        Assert.Equal(
            "[0.5, {\"$float\": \"nan\"}, {\"$float\": \"inf\"}, {\"$float\": \"-inf\"}, 1.0]",
            JudgeJson.Dumps(Codec.Vector([0.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.0])));
    }

    [Theory]
    [InlineData(typeof(string), "str")]
    [InlineData(typeof(long), "int")]
    [InlineData(typeof(double), "float")]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(ArgumentException), "ValueError")]
    public void ABuiltinClassHasItsPythonName(Type type, string python)
    {
        Assert.Equal($"{{\"$class\": \"{python}\"}}", Dumps(type));
    }

    [Fact]
    public void AnExceptionWithNoPythonNameIsAHostError()
    {
        Assert.Throws<JudgeHostErrorException>(() => Codec.Encode(new AggregateException(), new JudgeRegistry()));
        Assert.Throws<JudgeHostErrorException>(() => Codec.Encode(typeof(AggregateException), new JudgeRegistry()));
    }

    [Fact]
    public void ADateTimeAndACharAreUnknown()
    {
        Assert.Equal("{\"$unknown\": \"DateTime\"}", Dumps(DateTime.UnixEpoch));
        Assert.Equal("{\"$unknown\": \"Char\"}", Dumps('c'));
    }

    private static string Dumps(object? value) => JudgeJson.Dumps(Codec.Encode(value, new JudgeRegistry()));
}
