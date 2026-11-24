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

    await stdout.WriteLineAsync(result.ToJsonString());
}

return 0;

static async ValueTask<JsonNode> RunPureAsync(JsonElement root)
{
    var name = root.GetProperty("fn").GetString()!;
    if (!Bindings.Pure.TryGetValue(name, out var binding))
    {
        return new JsonObject { ["error"] = Bindings.NotBound };
    }

    var arguments = root.TryGetProperty("args", out var a) ? a : default;
    var keywords = root.TryGetProperty("kwargs", out var k) ? k : default;
    try
    {
        return new JsonObject { ["ok"] = await binding(arguments, keywords) };
    }
#pragma warning disable CA1031 // The exception's Python name is the result of the case.
    catch (Exception error)
#pragma warning restore CA1031
    {
        return new JsonObject { ["error"] = Codec.PythonExceptionName(error) };
    }
}
