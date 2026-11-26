// Support for the evaluation tests of the decisions client (graphiti_core/llm_client/openai_decisions_client.py and
// decision_prompts.py). The fake sits at the network boundary, where upstream puts httpx.MockTransport: an
// HttpMessageHandler that gives scripted replies and records each request. No test here sends a request.
namespace Graphiti.Port.Evaluation;

using System.Net;
using System.Text;
using System.Text.Json;
using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.PyCompat;

/// <summary>One request that the client sent: the method, the URL, the header values that matter, and the body.</summary>
internal sealed record DecisionRequest(string Method, string Url, string? Authorization, string? ContentType, string BodyText)
{
    /// <summary>The body as a value of the value model.</summary>
    internal OrderedDictionary<string, object?> Body => Py.AsDict(PyJson.Loads(BodyText));
}

/// <summary>The fake of the network: a handler that answers from a function and records each request.</summary>
internal sealed class ScriptedHandler(Func<DecisionRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    internal List<DecisionRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new DecisionRequest(
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            request.Headers.TryGetValues("Authorization", out var authorization) ? string.Join(",", authorization) : null,
            request.Content?.Headers.ContentType?.ToString(),
            body);
        Requests.Add(recorded);
        return respond(recorded);
    }
}

/// <summary>A span that keeps what the client writes to it, as the Python recording tracer does.</summary>
internal sealed class RecordingSpan(string name) : TracerSpan
{
    internal string Name { get; } = name;

    internal OrderedDictionary<string, object?> Attributes { get; } = [];

    internal List<string?>? Status { get; private set; }

    internal List<(Exception Error, string Message)> Exceptions { get; } = [];

    internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
    {
        foreach (var (key, value) in attributes)
        {
            Attributes[key] = value;
        }
    }

    internal override void SetStatus(string status, string? description = null)
    {
        Status = [status, description];
    }

    internal override void RecordException(Exception exception)
    {
        Exceptions.Add((exception, exception.Message));
    }
}

internal sealed class RecordingTracer : Tracer
{
    internal List<RecordingSpan> Spans { get; } = [];

    internal override async Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        var span = new RecordingSpan(name);
        Spans.Add(span);
        return await body(span);
    }
}

internal static class DecisionSupport
{
    internal const string Key = "test-key";

    internal const string BaseUrl = "https://decisions.test/v1";

    /// <summary>The text of an HTTP reply with a JSON body.</summary>
    internal static HttpResponseMessage Reply(int status, string? body, string contentType = "application/json") =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(body ?? string.Empty, Encoding.UTF8, contentType),
        };

    internal static HttpResponseMessage JsonReply(int status, object? value) =>
        Reply(status, PyJson.Dumps(value, ensureAscii: false, compact: true));

    internal static HttpClient HttpClientOf(ScriptedHandler handler) => new(handler);

    internal static OrderedDictionary<string, object?> Dict(params (string Key, object? Value)[] items)
    {
        var d = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in items)
        {
            d[key] = value;
        }

        return d;
    }

    /// <summary>The payload of the upstream tests: three questions, a duplicate, a contradiction, and a choice.</summary>
    internal static OrderedDictionary<string, object?> Payload(string state = "current state", string instructions = "caller instructions") =>
        Dict(
            ("instructions", instructions),
            ("state", state),
            ("questions", Dict(
                ("dup0", Dict(("type", "noul"), ("instructions", "Is this a duplicate?"), ("criteria", Dict(("true", "yes"), ("false", "no"))))),
                ("con0", Dict(("type", "predicate"), ("instructions", "Is this a contradiction?"))),
                ("pick", Dict(("type", "choice"), ("instructions", "Choose one."), ("criteria", Dict(("none", "No match"), ("alice", "Alice Smith"))))))));

    /// <summary>The questions of the payload as the typed dictionary that the client takes.</summary>
    internal static OrderedDictionary<string, OrderedDictionary<string, object?>> Questions(OrderedDictionary<string, object?> payload)
    {
        var questions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
        foreach (var (name, question) in Py.AsDict(payload["questions"]))
        {
            questions[name] = Py.AsDict(question);
        }

        return questions;
    }

    internal static List<object?> AnswerSet(double duplicate = 0.69, double contradiction = 0.79, string choice = "alice") =>
    [
        Dict(("type", "predicate"), ("name", "dup0"), ("probability", duplicate)),
        Dict(("type", "predicate"), ("name", "con0"), ("probability", contradiction)),
        Dict(
            ("type", "choice"),
            ("name", "pick"),
            ("choice", choice),
            ("probabilities", new List<object?>
            {
                Dict(("value", "none"), ("probability", 0.0)),
                Dict(("value", "alice"), ("probability", 1.0)),
            }),
            ("confidence", 1.0)),
    ];

    internal static OpenAIDecisionsClient Client(ScriptedHandler handler, long maxRetries = 5)
    {
        var client = new OpenAIDecisionsClient(
            config: new LLMConfig(apiKey: Key),
            baseUrl: BaseUrl,
            httpClient: HttpClientOf(handler),
            maxRetries: maxRetries);
        return client;
    }

    internal static Task<OrderedDictionary<string, object?>> Call(OpenAIDecisionsClient client, OrderedDictionary<string, object?>? payload = null) =>
        client.GenerateResponseAsync(
            [new Message { Role = "user", Content = PyJson.Dumps(payload ?? Payload()) }.Validate()],
            promptName: "dedupe_edges.resolve_edge",
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>A deep comparison as Python <c>==</c> on dicts does it: the order of the keys does not matter.</summary>
    internal static void AssertEqualValues(object? expected, object? actual) =>
        Assert.Equal(PyJson.Dumps(expected, sortKeys: true), PyJson.Dumps(actual, sortKeys: true));

    /// <summary>The Python class name that the port gives an exception, for the comparison with a recording.</summary>
    internal static string PythonName(Exception error) => error switch
    {
        OpenAIDecisionsInputTooLongException => "OpenAIDecisionsInputTooLongError",
        RateLimitException => "RateLimitError",
        JsonException => "JSONDecodeError",
        HttpRequestException { StatusCode: not null } => "HTTPStatusError",
        HttpRequestException => "ConnectError",
        TimeoutException => "ReadTimeout",
        InvalidOperationException => "RuntimeError",
        KeyNotFoundException => "KeyError",
        InvalidCastException => "TypeError",
        ArgumentException => "ValueError",
        _ => error.GetType().Name,
    };
}
