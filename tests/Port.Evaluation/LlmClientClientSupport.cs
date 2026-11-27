// Shared helpers of the evaluation tests for graphiti_core/llm_client/client.py, embedder/client.py, and
// cross_encoder/client.py. values/llm_client.json holds what port/tools/record_llm_client.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.Net;
using System.Reflection;
using Graphiti.Core;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static class LlmClientClientSupport
{
    internal static readonly OrderedDictionary<string, object?> Document = Load();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "llm_client.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    internal static OrderedDictionary<string, object?> Dict(object? value) => Py.AsDict(value);

    internal static List<object?> Items(object? value) => Py.AsList(value);

    /// <summary>
    /// The LLMConfig of a recorded call (`LLMConfig(**kwargs)`): the case with no keyword argument calls the constructor
    /// with none, and each other case passes only the arguments that it names. A parameter that the call leaves out takes
    /// the default of the C# constructor, so a wrong default shows. Reflection leaves out an argument with Type.Missing.
    /// </summary>
    internal static LLMConfig MakeConfig(OrderedDictionary<string, object?> kwargs)
    {
        var names = new Dictionary<string, string>
        {
            ["apiKey"] = "api_key",
            ["model"] = "model",
            ["baseUrl"] = "base_url",
            ["temperature"] = "temperature",
            ["maxTokens"] = "max_tokens",
            ["smallModel"] = "small_model",
        };
        if (kwargs.Count == 0)
        {
            return new LLMConfig();
        }

        var constructor = typeof(LLMConfig).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var arguments = constructor.GetParameters().Select(p =>
        {
            if (!kwargs.TryGetValue(names[p.Name!], out var value))
            {
                return Type.Missing;
            }

            return p.Name switch
            {
                "temperature" => Py.AsDouble(value),
                "maxTokens" => Py.AsLong(value),
                _ => value,
            };
        }).ToArray();
        return (LLMConfig)constructor.Invoke(BindingFlags.OptionalParamBinding, null, arguments, null);
    }

    /// <summary>The Message list of a recorded list of [role, content] pairs.</summary>
    internal static List<Message> Messages(object? pairs) =>
        Items(pairs).Select(p => Items(p)).Select(p => new Message { Role = (string)p[0]!, Content = (string)p[1]! }).ToList();

    /// <summary>[[role, content], ...] as the recording writes it.</summary>
    internal static List<object?> Pairs(List<Message> messages) =>
        messages.Select(m => (object?)new List<object?> { m.Role, m.Content }).ToList();

    /// <summary>
    /// The exception that a recorded spec names (port/tools/record_llm_client.py, make_error) in the form that R4 gives.
    /// </summary>
    internal static Exception MakeError(OrderedDictionary<string, object?> spec)
    {
        var kind = (string)spec["kind"]!;
        var message = spec.TryGetValue("message", out var m) ? (string)m! : "m";
        return kind switch
        {
            "RateLimitError" => new RateLimitException(message),
            "EmptyResponseError" => new EmptyResponseException(message),
            "RefusalError" => new RefusalException(message),
            "JSONDecodeError" => new System.Text.Json.JsonException(message),
            "HTTPStatusError" => new HttpRequestException(message, null, (HttpStatusCode)Py.AsLong(spec["status"])),
            "TransportError" => new HttpRequestException(message),
            "ConnectError" => new HttpRequestException(message),
            "ReadTimeout" => new TimeoutException(message),
            "TimeoutError" => new TimeoutException(message),
            "ValueError" => new ArgumentException(message),
            "KeyError" => new KeyNotFoundException(message),
            "RuntimeError" => new InvalidOperationException(message),
            "OSError" => new IOException(message),
            "Exception" => new PlainException(message),
            _ => throw new InvalidOperationException(kind),
        };
    }

    /// <summary>The plain `Exception` of Python: an exception that no clause of the retry names.</summary>
    internal sealed class PlainException(string message) : Exception(message);

    /// <summary>The subclass of LLMClient that the upstream test calls MockLLMClient, with a scripted reply.</summary>
    internal sealed class ScriptedClient(string pythonName, LLMConfig? config = null, bool cache = false) : LLMClient(config, cache)
    {
        internal List<RecordedCall> Calls { get; } = [];

        internal Func<int, OrderedDictionary<string, object?>> Script { get; set; } = _ => new() { ["content"] = "test" };

        internal override string PythonName { get; } = pythonName;

        internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
            List<Message> messages,
            IResponseModel? responseModel = null,
            long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
            ModelSize modelSize = ModelSize.Medium,
            string? model = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RecordedCall(Pairs(messages), responseModel?.PythonName, maxTokens, (string)Py.EnumValue(modelSize), model, Asyncio.IsOnLoop));
            return Task.FromResult(Script(Calls.Count - 1));
        }
    }

    internal sealed record RecordedCall(List<object?> Messages, string? ResponseModel, long MaxTokens, string ModelSize, string? Model, bool OnLoop);

    /// <summary>The tracer of the recording: each call of the span writes an event, as in the recorder.</summary>
    internal sealed class RecordingTracer(List<object?> events) : Tracer
    {
        internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
        {
            events.Add(new List<object?> { "start_span", name });
            return await body(new RecordingSpan(events));
        }
    }

    private sealed class RecordingSpan(List<object?> events) : TracerSpan
    {
        internal override void AddAttributes(OrderedDictionary<string, object?> attributes) =>
            events.Add(new List<object?> { "add_attributes", new OrderedDictionary<string, object?>(attributes) });

        internal override void SetStatus(string status, string? description = null) =>
            events.Add(new List<object?> { "set_status", status, description });

        internal override void RecordException(Exception exception) =>
            events.Add(new List<object?> { "record_exception", exception.Message });
    }

    /// <summary>
    /// A clock that never waits. Each timer that is set for a time fires at once on the thread pool, and its due time is
    /// kept in <see cref="Delays"/>.
    /// </summary>
    internal sealed class InstantTimeProvider : TimeProvider
    {
        internal List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new InstantTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        private sealed class InstantTimer(InstantTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    lock (owner.Delays)
                    {
                        owner.Delays.Add(dueTime);
                    }

                    ThreadPool.QueueUserWorkItem(_ => callback(state));
                }

                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>A logger factory that keeps each line, as caplog does.</summary>
    internal sealed class CaptureFactory : ILoggerFactory
    {
        internal List<(LogLevel Level, string Message)> Lines { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(LogLevel, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (lines)
                {
                    lines.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
