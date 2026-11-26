namespace Port.Evaluation.JudgeHost;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>One case line in, one result line out, through <see cref="HostLoop"/>.</summary>
public sealed class HostLoopTests
{
    private static readonly Dictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> Pure = new(StringComparer.Ordinal)
    {
        ["t:upper"] = call => Codec.Result(call.Signature("text").String("text").ToUpperInvariant()),
        ["t:key"] = _ => throw new KeyNotFoundException("'x'"),
        ["t:aggregate"] = _ => throw new AggregateException(new KeyNotFoundException()),
        ["t:decimal"] = _ => ValueTask.FromResult<JsonNode?>(JsonValue.Create(1.5m)),
        ["t:unnamed"] = call => Codec.Result(call.Signature("text").String("other")),
        ["t:gather"] = async call =>
        {
            var n = call.Signature("n").Long("n");
            var results = await Asyncio.GatherAsync<long>([async () => { await Task.Yield(); return n; }, () => Task.FromResult(n + 1)]);
            return Codec.Encode(results, new JudgeRegistry());
        },
        ["t:invoke"] = call => call.Signature("a", "b").InvokeAsync(Add),
    };

    [Theory]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:none\", \"args\": []}", "{\"error\": \"JudgeNotBound\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:upper\", \"args\": [\"ab\"]}", "{\"ok\": \"AB\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:upper\", \"args\": [42]}", "{\"error\": \"JudgeTypeRejected\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:upper\", \"args\": [{\"$model\": \"m:M\", \"data\": {}}]}", "{\"error\": \"JudgeNotBound\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:upper\", \"args\": [{\"$dt\": \"bad\"}]}", "{\"error\": \"ValueError\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:key\", \"args\": []}", "{\"error\": \"KeyError\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:aggregate\", \"args\": []}", "{\"host_error\": \"AggregateException\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:decimal\", \"args\": []}", "{\"host_error\": \"InvalidOperationException\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:unnamed\", \"args\": [\"a\"]}", "{\"host_error\": \"JudgeHostErrorException\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:gather\", \"args\": [4]}", "{\"ok\": [4, 5]}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:invoke\", \"args\": [2], \"kwargs\": {\"b\": 3}}", "{\"ok\": 5}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"t:invoke\", \"args\": [2]}", "{\"ok\": 12}")]
    [InlineData("{\"id\": \"x\", \"steps\": []}", "{\"steps\": 0}")]
    [InlineData("not json", "{\"host_error\": \"JsonReaderException\"}")]
    public async Task ALineGivesItsResultLine(string line, string expected)
    {
        Assert.Equal(expected, await HostLoop.AnswerAsync(line, Pure, new JudgeRegistry(), RunFlowAsync));
    }

    [Fact]
    public async Task TheHostBindingsAnswerTheEmbedCase()
    {
        var line = await HostLoop.AnswerAsync("{\"id\": \"x\", \"fn\": \"judge:embed\", \"args\": [\"Hello\"]}");
        Assert.StartsWith("{\"ok\": [", line, StringComparison.Ordinal);
        Assert.Equal("{\"error\": \"JudgeNotBound\"}", await HostLoop.AnswerAsync("{\"id\": \"x\", \"fn\": \"m:f\", \"args\": []}"));
    }

    private static long Add(long a, long b = 10) => a + b;

    // The flow runs on the scheduler as a binding does.
    private static ValueTask<JsonNode> RunFlowAsync(JsonElement scenario) =>
        ValueTask.FromResult<JsonNode>(new JsonObject { ["steps"] = Asyncio.IsOnLoop ? scenario.GetProperty("steps").GetArrayLength() : -1 });
}
