namespace Graphiti.PyCompat.Tests;

using System.Globalization;
using System.Text.Json;
using Graphiti.PyCompat;

/// <summary>
/// Runs the scripts of <c>python-values.asyncio.json</c> on the <see cref="Asyncio"/> helpers and gives the trace.
/// <c>port/tools/generate_asyncio_values.py</c> runs the same scripts on asyncio and documents each operation.
/// Each operation calls the helper that RULES.md R8 names for the Python call, with the token of the coroutine that
/// runs it, as ported code does.
/// </summary>
internal sealed class AsyncioScripts
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.asyncio.json")));

    private readonly JsonElement coros;
    private readonly JsonElement main;
    private readonly long semaphoreLimit;
    private readonly List<string> trace = [];
    private readonly Dictionary<string, (Task<object?> Task, CancellationTokenSource Cancel)> tasks = [];
    private readonly Dictionary<string, AsyncEvent> events = [];

    private AsyncioScripts(JsonElement script)
    {
        coros = script.GetProperty("coros");
        main = script.GetProperty("main");
        semaphoreLimit = Meta.GetProperty("semaphore_limit").GetInt64();
    }

    internal static JsonElement Meta => Document.RootElement.GetProperty("meta");

    internal static IEnumerable<JsonElement> Scripts => Document.RootElement.GetProperty("scripts").EnumerateArray();

    internal static JsonElement Script(string id) => Scripts.Single(s => s.GetProperty("id").GetString() == id);

    /// <summary>The scripts where the port differs from asyncio by a rule of R8. Their trace is the asyncio trace.</summary>
    internal static JsonElement Difference(string id) =>
        Document.RootElement.GetProperty("differs").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);

    /// <summary>Runs the script on the scheduler and returns its trace up to the end of <c>main</c>.</summary>
    internal static Task<List<string>> TraceAsync(JsonElement script) => Asyncio.Run(async () =>
    {
        var run = new AsyncioScripts(script);
        try
        {
            var result = await run.RunAsync("main", CancellationToken.None);
            run.Log("main", $"-> {Fmt(result)}");
        }
        catch (Exception e)
        {
            run.Log("main", $"raised {Fmt(e)}");
        }

        return run.trace.ToList();
    });

    /// <summary>The text of a value in a trace, as <c>fmt</c> of the generator writes it.</summary>
    internal static string Fmt(object? value) => value switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        long n => n.ToString(CultureInfo.InvariantCulture),
        System.Collections.IList list => "[" + string.Join(", ", list.Cast<object?>().Select(Fmt)) + "]",
        OperationCanceledException => "CancelledError",
        ArgumentException e => $"ValueError({e.Message})",
        InvalidOperationException e => $"RuntimeError({e.Message})",
        TimeoutException e => $"TimeoutError({e.Message})",
        _ => throw new InvalidCastException($"no trace text for {value.GetType().Name}: {value}"),
    };

    private static Exception Error(string type, string message) => type switch
    {
        "ValueError" => new ArgumentException(message),
        "RuntimeError" => new InvalidOperationException(message),
        "TimeoutError" => new TimeoutException(message),
        _ => throw new ArgumentException($"unknown error {type}"),
    };

    private static long? Value(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt64();

    private static string Text(JsonElement op, int i) => op[i].GetString() ?? throw new InvalidDataException("text expected");

    private static List<string> Names(JsonElement e) => [.. e.EnumerateArray().Select(n => n.GetString() ?? string.Empty)];

    private void Log(string me, string text) => trace.Add($"{me}:{text}");

    private AsyncEvent Event(string name)
    {
        if (!events.TryGetValue(name, out var e))
        {
            e = new AsyncEvent();
            events[name] = e;
        }

        return e;
    }

    private async Task<object?> RunAsync(string name, CancellationToken token)
    {
        var (_, value) = await BlockAsync(name, name == "main" ? main : coros.GetProperty(name), token);
        return value;
    }

    private async Task<object?> TrackedAsync(long[] state, string name, CancellationToken token)
    {
        state[0]++;
        state[1] = Math.Max(state[1], state[0]);
        try
        {
            return await RunAsync(name, token);
        }
        finally
        {
            state[0]--;
        }
    }

    private async Task<(bool Returned, object? Value)> BlockAsync(string me, JsonElement ops, CancellationToken token)
    {
        foreach (var op in ops.EnumerateArray())
        {
            switch (Text(op, 0))
            {
                case "log":
                    Log(me, Text(op, 1));
                    break;
                case "yield":
                    // asyncio.sleep(0): one trip through the ready queue, and a cancellation point.
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    break;
                case "raise":
                    throw Error(Text(op, 1), Text(op, 2));
                case "return":
                    return (true, Value(op[1]));
                case "call":
                    {
                        var r = await RunAsync(Text(op, 1), token);
                        Log(me, $"call {Text(op, 1)} -> {Fmt(r)}");
                        break;
                    }

                case "try":
                    {
                        var mode = Text(op, 2);
                        try
                        {
                            var (returned, value) = await BlockAsync(me, op[1], token);
                            if (returned)
                            {
                                return (true, value);
                            }
                        }
                        catch (Exception e)
                        {
                            Log(me, $"caught {Fmt(e)}");
                            if (op.GetArrayLength() > 3)
                            {
                                await BlockAsync(me, op[3], token);
                            }

                            if (e is OperationCanceledException ? mode != "all" : mode == "reraise")
                            {
                                throw;
                            }
                        }

                        break;
                    }

                case "gather":
                    {
                        List<Func<Task<object?>>> calls = [.. Names(op[1]).Select(n => (Func<Task<object?>>)(() => RunAsync(n, token)))];
                        object r = op[2].GetBoolean()
                            ? await Asyncio.GatherAsync(calls, returnExceptions: true, token)
                            : await Asyncio.GatherAsync(calls, token);
                        Log(me, $"gather -> {Fmt(r)}");
                        break;
                    }

                case "sgather":
                    {
                        // The port of semaphore_gather resolves `max_coroutines or SEMAPHORE_LIMIT` (R8).
                        var limit = Value(op[2]) is long n && n != 0 ? n : semaphoreLimit;
                        var state = new long[2];
                        List<Func<Task<object?>>> calls = [.. Names(op[1]).Select(n => (Func<Task<object?>>)(() => TrackedAsync(state, n, token)))];
                        var r = await Asyncio.SemaphoreGatherAsync(calls, limit, token);
                        Log(me, $"sgather -> {Fmt(r)} max={state[1]}");
                        break;
                    }

                case "create_task":
                    {
                        var name = Text(op, 1);
                        var task = Asyncio.CreateTask(t => RunAsync(name, t), out var cancel);
                        tasks[Text(op, 2)] = (task, cancel);
                        break;
                    }

                case "cancel":
                    tasks[Text(op, 1)].Cancel.Cancel();
                    break;
                case "await":
                    {
                        var r = await tasks[Text(op, 1)].Task;
                        Log(me, $"await {Text(op, 1)} -> {Fmt(r)}");
                        break;
                    }

                case "shield":
                    {
                        var r = await Asyncio.ShieldAsync(tasks[Text(op, 1)].Task, token);
                        Log(me, $"shield {Text(op, 1)} -> {Fmt(r)}");
                        break;
                    }

                case "gather_tasks":
                    {
                        List<Task<object?>> existing = [.. Names(op[1]).Select(h => tasks[h].Task)];
                        var r = await Asyncio.GatherAsync(existing, op[2].GetBoolean());
                        Log(me, $"gather_tasks -> {Fmt(r)}");
                        break;
                    }

                case "wait_for":
                    {
                        var name = Text(op, 1);
                        double? timeout = op[2].ValueKind == JsonValueKind.Null ? null : op[2].GetDouble();
                        var r = await Asyncio.WaitForAsync(t => RunAsync(name, t), timeout, TimeProvider.System, token);
                        Log(me, $"wait_for {name} -> {Fmt(r)}");
                        break;
                    }

                case "ev_wait":
                    {
                        var r = await Event(Text(op, 1)).WaitAsync(token);
                        Log(me, $"ev_wait {Text(op, 1)} -> {Fmt(r)}");
                        break;
                    }

                case "ev_set":
                    Event(Text(op, 1)).Set();
                    break;
                case "ev_clear":
                    Event(Text(op, 1)).Clear();
                    break;
                case "ev_is_set":
                    Log(me, $"ev_is_set {Text(op, 1)} -> {Fmt(Event(Text(op, 1)).IsSet())}");
                    break;
                case "to_thread":
                    {
                        var value = Value(op[1]);
                        var r = await Asyncio.ToThreadAsync(() => value, token);
                        Log(me, $"to_thread -> {Fmt(r)}");
                        break;
                    }

                default:
                    throw new InvalidDataException($"unknown operation {op}");
            }
        }

        return (false, null);
    }
}
