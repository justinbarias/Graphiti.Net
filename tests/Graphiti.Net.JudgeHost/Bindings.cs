namespace Graphiti.JudgeHost;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>
/// Maps each judge function name to the ported method. A ported unit adds its entries here.
/// A binding can do three things only: decode the arguments, call the product code, and encode the result.
/// It must not branch on the case, fill in defaults, or repair output. REVIEW.md checks this.
/// </summary>
public static class Bindings
{
    public const string NotBound = "JudgeNotBound";

    public static Dictionary<string, Func<JsonElement, JsonElement, ValueTask<JsonNode?>>> Pure { get; } = new(StringComparer.Ordinal)
    {
        ["judge:embed"] = (args, _) => ValueTask.FromResult<JsonNode?>(Codec.Vector(JudgeEmbedding.Embed(args[0].GetString()!))),
    };

    /// <summary>Runs a flow scenario. Not bound until the facade is ported (plan 0004, layer 9).</summary>
    public static ValueTask<JsonNode> RunFlowAsync(JsonElement scenario)
    {
        _ = scenario;
        return ValueTask.FromResult<JsonNode>(new JsonObject { ["host_error"] = NotBound });
    }
}
