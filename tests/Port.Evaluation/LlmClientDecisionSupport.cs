// Support for the evaluation tests of the decisions client (graphiti_core/llm_client/openai_decisions_client.py and
// decision_prompts.py). The fake sits at the network boundary, where upstream puts httpx.MockTransport: an
// HttpMessageHandler that gives scripted replies and records each request. No test here sends a request.
namespace Graphiti.Port.Evaluation;

using System.Net;
using System.Net.Sockets;
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
internal sealed class ScriptedHandler(Func<DecisionRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    internal ScriptedHandler(Func<DecisionRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

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
        lock (Requests)
        {
            Requests.Add(recorded);
        }

        return await respond(recorded, cancellationToken);
    }
}

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>, and then it fires each timer that is due. The client
/// ends a request that takes too long with a timer of <c>PortServices.Time</c>.
/// </summary>
internal sealed class DecisionClock : TimeProvider
{
    private readonly List<Timer> timers = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (timers)
        {
            return ticks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    internal void Advance(TimeSpan by)
    {
        List<Timer> due;
        lock (timers)
        {
            ticks += by.Ticks;
            due = timers.Where(t => t.Due is { } at && at <= ticks).ToList();
            foreach (var timer in due)
            {
                timer.Due = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class Timer(DecisionClock clock, TimerCallback callback, object? state) : ITimer
    {
        internal long? Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.timers)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.ticks + dueTime.Ticks;
                if (!clock.timers.Contains(this))
                {
                    clock.timers.Add(this);
                }
            }

            return true;
        }

        internal void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock.timers)
            {
                Due = null;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A server on 127.0.0.1 for the one test of the default client. It answers <c>POST /v1/decisions</c> with a redirect and
/// each other request with a valid reply, and it keeps the request line of each request.
/// </summary>
internal sealed class RedirectServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly CancellationTokenSource stop = new();

    internal RedirectServer()
    {
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    internal List<string> Requests { get; } = [];

    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var connection = await listener.AcceptTcpClientAsync(stop.Token);
                _ = Task.Run(() => ServeAsync(connection));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The test has ended.
        }
    }

    private async Task ServeAsync(TcpClient connection)
    {
        using (connection)
        {
            var stream = connection.GetStream();
            var head = new List<byte>();
            var one = new byte[1];
            while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
            {
                if (await stream.ReadAsync(one, stop.Token) == 0)
                {
                    return;
                }

                head.Add(one[0]);
            }

            var lines = Encoding.Latin1.GetString([.. head]).Split("\r\n");
            var length = lines.Where(line => line.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase)).Select(line => int.Parse(line[15..])).FirstOrDefault();
            var body = new byte[length];
            for (var read = 0; read < length;)
            {
                read += await stream.ReadAsync(body.AsMemory(read), stop.Token);
            }

            var requestLine = lines[0][..lines[0].LastIndexOf(' ')];
            lock (Requests)
            {
                Requests.Add(requestLine);
            }

            const string valid = "{\"answers\": [{\"type\": \"predicate\", \"name\": \"dup0\", \"probability\": 0.9}]}";
            var reply = requestLine.StartsWith("POST /v1/decisions", StringComparison.Ordinal)
                ? "HTTP/1.1 302 Found\r\nLocation: /v1/other\r\nContent-Length: 5\r\nConnection: close\r\n\r\nmoved"
                : "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + valid.Length + "\r\nConnection: close\r\n\r\n" + valid;
            await stream.WriteAsync(Encoding.Latin1.GetBytes(reply), stop.Token);
        }
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

internal sealed class DecisionRecordingTracer : Tracer
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

    /// <summary>A reply with the bytes of its body and the text of its Content-Type header, as the recording gives them.</summary>
    internal static HttpResponseMessage RawReply(int status, byte[] data, string contentType)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(data) };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return response;
    }

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

    internal static Task<OrderedDictionary<string, object?>> Call(
        OpenAIDecisionsClient client,
        OrderedDictionary<string, object?>? payload = null,
        CancellationToken? cancellationToken = null) =>
        client.GenerateResponseAsync(
            [new Message { Role = "user", Content = PyJson.Dumps(payload ?? Payload()) }.Validate()],
            promptName: "dedupe_edges.resolve_edge",
            cancellationToken: cancellationToken ?? TestContext.Current.CancellationToken);

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
        IOException => "ReadError",
        OperationCanceledException => "CancelledError",
        NotImplementedException => "NotImplementedError",
        MissingMemberException => "AttributeError",
        EncoderFallbackException => "UnicodeEncodeError",
        DecoderFallbackException => "UnicodeDecodeError",
        OverflowException => "OverflowError",
        InvalidOperationException => "RuntimeError",
        KeyNotFoundException => "KeyError",
        InvalidCastException => "TypeError",
        ArgumentException => "ValueError",
        _ => error.GetType().Name,
    };
}
