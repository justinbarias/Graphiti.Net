using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost;
using Graphiti.JudgeHost.Core;

// The .NET judge host. It reads one case as a JSON line from standard input and prints one
// JSON result line. port/judge/README.md defines the protocol.
// Nothing but result lines may reach standard output.

if (args is ["--selfcheck", var vectorsPath])
{
    return JudgeEmbedding.SelfCheck(vectorsPath);
}

var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
Console.SetOut(Console.Error);
using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

while (await stdin.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    JsonNode result;
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        result = root.TryGetProperty("steps", out _)
            ? await Bindings.RunFlowAsync(root)
            : await RunPureAsync(root);
    }
#pragma warning disable CA1031 // The host must answer every case, whatever the product code throws.
    catch (Exception error)
#pragma warning restore CA1031
    {
        await Console.Error.WriteLineAsync(error.ToString());
        result = new JsonObject { ["host_error"] = error.GetType().Name };
    }

    // json.dumps(result, ensure_ascii=True), as host.py writes it. A lone surrogate stays an escape.
    await stdout.WriteLineAsync(Codec.Line(result));
}

return 0;

static async ValueTask<JsonNode> RunPureAsync(JsonElement root)
{
    var name = root.GetProperty("fn").GetString()!;
    if (!Bindings.Pure.TryGetValue(name, out var binding))
    {
        return new JsonObject { ["error"] = Bindings.NotBound };
    }

    try
    {
        // host.py decodes inside the same try, so an argument that does not decode gives its Python error.
        var call = JudgeArguments.Decode(root, new ValueDecoder(Bindings.Registry));
        return new JsonObject { ["ok"] = await binding(call) };
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
#pragma warning disable CA1031 // The exception's Python name is the result of the case.
    catch (Exception error)
#pragma warning restore CA1031
    {
        return new JsonObject { ["error"] = Codec.PythonExceptionName(error) };
    }
}
