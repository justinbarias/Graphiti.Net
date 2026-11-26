namespace Port.Evaluation.JudgeHost;

using System.Text.Json;
using Graphiti.JudgeHost.Core;

/// <summary><see cref="JudgeParameters.InvokeAsync"/>: a binding that names the parameters and passes a method group.</summary>
public sealed class InvokeTests
{
    [Fact]
    public async Task AMethodWithNoDefault()
    {
        Assert.Equal("\"AB\"", await Invoke("[\"ab\"]", "{}", ["text"], Upper));
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[]", "{}", ["text"], Upper));
    }

    [Fact]
    public async Task AMethodWithOneDefault()
    {
        Assert.Equal("\"ab|3\"", await Invoke("[\"ab\"]", "{}", ["text", "n"], OneDefault));
        Assert.Equal("\"ab|5\"", await Invoke("[\"ab\", 5]", "{}", ["text", "n"], OneDefault));
        Assert.Equal("\"ab|7\"", await Invoke("[]", "{\"n\": 7, \"text\": \"ab\"}", ["text", "n"], OneDefault));
    }

    [Fact]
    public async Task AMethodWithTwoDefaults()
    {
        Assert.Equal("\"x|1|-\"", await Invoke("[\"x\"]", "{}", ["text", "n", "sep"], TwoDefaults));
        Assert.Equal("\"x|1|+\"", await Invoke("[\"x\"]", "{\"sep\": \"+\"}", ["text", "n", "sep"], TwoDefaults));
        Assert.Equal("\"x|4|-\"", await Invoke("[\"x\", 4]", "{}", ["text", "n", "sep"], TwoDefaults));
        Assert.Equal("\"x|4|+\"", await Invoke("[\"x\", 4, \"+\"]", "{}", ["text", "n", "sep"], TwoDefaults));
    }

    [Fact]
    public async Task ANullableParameterTakesNone()
    {
        Assert.Equal("\"none\"", await Invoke("[null]", "{}", ["group"], Nullable));
        Assert.Equal("\"g\"", await Invoke("[\"g\"]", "{}", ["group"], Nullable));
        Assert.Equal("null", await Invoke("[null]", "{}", ["n"], NullableLong));
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[null]", "{}", ["text"], Upper));
    }

    [Fact]
    public async Task AListParameter()
    {
        Assert.Equal("[\"b\", \"a\"]", await Invoke("[[\"a\", \"b\"]]", "{}", ["items"], Reverse));
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[[\"a\", 1]]", "{}", ["items"], Reverse));
    }

    [Fact]
    public async Task AnAsyncMethodIsAwaited()
    {
        Assert.Equal("4", await Invoke("[2]", "{}", ["n"], TwiceAsync));
        Assert.Equal("4", await Invoke("[2]", "{}", ["n"], TwiceValueAsync));
        Assert.Equal("null", await Invoke("[2]", "{}", ["n"], NothingAsync));
        Assert.Equal("null", await Invoke("[2]", "{}", ["n"], NothingValueAsync));
        Assert.Equal("2", await Invoke("[2]", "{}", ["n"], WithTokenAsync));
    }

    [Fact]
    public async Task AnExceptionOfTheMethodKeepsItsTypeAndStack()
    {
        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoke("[\"k\"]", "{}", ["key"], Throws));
        Assert.Contains(nameof(Throws), error.StackTrace, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidCastException>(() => Invoke("[1]", "{}", ["n"], ThrowsAsync));
    }

    [Fact]
    public async Task AWrongTypeIsRejected()
    {
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[42]", "{}", ["text"], Upper));
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[\"x\", 1.5]", "{}", ["text", "n"], OneDefault));
        await Assert.ThrowsAsync<JudgeTypeRejectedException>(() => Invoke("[\"x\", true]", "{}", ["text", "n"], OneDefault));
    }

    [Fact]
    public async Task ASignatureThatDoesNotFitIsAHostError()
    {
        await Assert.ThrowsAsync<JudgeHostErrorException>(() => Invoke("[\"x\"]", "{}", ["text", "extra"], Upper));
        await Assert.ThrowsAsync<JudgeHostErrorException>(() => Invoke("[]", "{}", [], Upper));
    }

    private static async Task<string> Invoke(string args, string kwargs, string[] names, Delegate method)
    {
        using var document = JsonDocument.Parse($"{{\"args\": {args}, \"kwargs\": {kwargs}}}");
        var call = JudgeArguments.Decode(document.RootElement, new ValueDecoder(new JudgeRegistry()));
        return JudgeJson.Dumps(await call.Signature(names).InvokeAsync(method));
    }

    // The method groups below convert to Delegate with no cast.
    private static string Upper(string text) => text.ToUpperInvariant();

    private static string OneDefault(string text, long n = 3) => $"{text}|{n}";

    private static string TwoDefaults(string text, long n = 1, string sep = "-") => $"{text}|{n}|{sep}";

    private static string Nullable(string? group) => group ?? "none";

    private static long? NullableLong(long? n) => n;

    private static List<string> Reverse(List<string> items) => [.. Enumerable.Reverse(items)];

    private static async Task<long> TwiceAsync(long n)
    {
        await Task.Yield();
        return n * 2;
    }

    private static async ValueTask<long> TwiceValueAsync(long n)
    {
        await Task.Yield();
        return n * 2;
    }

    private static async Task NothingAsync(long n)
    {
        _ = n;
        await Task.Yield();
    }

    private static async ValueTask NothingValueAsync(long n)
    {
        _ = n;
        await Task.Yield();
    }

    private static Task<long> WithTokenAsync(long n, CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? Task.FromResult(-1L) : Task.FromResult(n);

    private static string Throws(string key) => throw new KeyNotFoundException(key);

    private static async Task<long> ThrowsAsync(long n)
    {
        await Task.Yield();
        throw new InvalidCastException(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
