namespace Graphiti.JudgeHost.Core;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.PyCompat;

/// <summary>
/// One turn of the protocol loop: a case line in, a result line out. port/judge/README.md defines the protocol.
/// Each binding and each flow runs on the scheduler of decision D22 through <see cref="Asyncio.Run{T}(Func{Task{T}})"/>,
/// as rule R8 asks of the judge host. The method never throws: a defect gives a <c>host_error</c> line.
/// </summary>
internal static class HostLoop
{
    /// <summary>Answers one case with the bindings of <see cref="Bindings"/>.</summary>
    internal static Task<string> AnswerAsync(string line) =>
        AnswerAsync(line, Bindings.Pure, Bindings.Registry, Bindings.RunFlowAsync);

    /// <summary>Answers one case with the given bindings, registry, and flow runner.</summary>
    internal static async Task<string> AnswerAsync(
        string line,
        IReadOnlyDictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> pure,
        JudgeRegistry registry,
        Func<JsonElement, ValueTask<JsonNode>> runFlow)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var result = root.TryGetProperty("steps", out _)
                ? await Asyncio.Run(async () => await runFlow(root))
                : await RunPureAsync(root, pure, registry);

            // json.dumps(result, ensure_ascii=True), as host.py writes it. A lone surrogate stays an escape.
            return Codec.Line(result);
        }
#pragma warning disable CA1031 // The host must answer every case, whatever the product code throws.
        catch (Exception error)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync(error.ToString());
            return HostError(error);
        }
    }

    private static string HostError(Exception error) =>
        Codec.Line(new JsonObject { ["host_error"] = error.GetType().Name });

    private static async Task<JsonNode> RunPureAsync(
        JsonElement root,
        IReadOnlyDictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> pure,
        JudgeRegistry registry)
    {
        var name = root.GetProperty("fn").GetString()!;
        if (!pure.TryGetValue(name, out var binding))
        {
            return new JsonObject { ["error"] = Bindings.NotBound };
        }

        try
        {
            // host.py decodes inside the same try, so an argument that does not decode gives its Python error.
            // A $model factory is product code, so the decoding runs on the scheduler too.
            var ok = await Asyncio.Run(async () => await binding(JudgeArguments.Decode(root, new ValueDecoder(registry))));
            return new JsonObject { ["ok"] = ok };
        }
        catch (JudgeNotBoundException error)
        {
            await Console.Error.WriteLineAsync($"judge: {name}: {error.Message}");
            return new JsonObject { ["error"] = Bindings.NotBound };
        }
        catch (JudgeTypeRejectedException error)
        {
            await Console.Error.WriteLineAsync($"judge: {name}: {error.Message}");
            return new JsonObject { ["error"] = Bindings.TypeRejected };
        }
        catch (Exception error) when (error is not JudgeHostErrorException && Codec.PythonExceptionName(error) is not null)
        {
            // The exception's Python name is the result of the case.
            return new JsonObject { ["error"] = Codec.PythonExceptionName(error) };
        }
    }
}
