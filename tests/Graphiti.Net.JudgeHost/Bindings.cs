namespace Graphiti.JudgeHost;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>
/// Maps each judge function name to the ported method. A ported unit adds its entries here.
/// A binding can do three things only: decode the arguments, call the product code, and encode the result.
/// It must not branch on the case, fill in defaults, or repair output. REVIEW.md checks this.
/// </summary>
/// <example>
/// The usual form names the Python parameters in order and lets <see cref="JudgeParameters.InvokeAsync"/> read,
/// convert, and default each one. A constructor, a property, or a method on an instance uses the explicit reads.
/// <code>
/// ["graphiti_core.helpers:lucene_sanitize"] = call => call.Signature("query").InvokeAsync(Helpers.LuceneSanitize),
/// ["graphiti_core.nodes:EntityNode.name"] = call => Codec.Result(call.Signature("self").Model&lt;EntityNode&gt;("self").Name),
/// </code>
/// </example>
internal static class Bindings
{
    public const string NotBound = "JudgeNotBound";

    public const string TypeRejected = "JudgeTypeRejected";

    /// <summary>
    /// Gets the <c>$ref</c>, <c>$enum</c>, and <c>$model</c> paths and the encoders of product types.
    /// A ported unit registers its paths and its model types here.
    /// </summary>
    internal static JudgeRegistry Registry { get; } = new();

    internal static Dictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> Pure { get; } = new(StringComparer.Ordinal)
    {
        ["judge:embed"] = call => call.Signature("text").InvokeAsync(JudgeEmbedding.Embed),
    };

    /// <summary>Runs a flow scenario. Not bound until the facade is ported (plan 0004, layer 9).</summary>
    public static ValueTask<JsonNode> RunFlowAsync(JsonElement scenario)
    {
        _ = scenario;
        return ValueTask.FromResult<JsonNode>(new JsonObject { ["host_error"] = NotBound });
    }
}
