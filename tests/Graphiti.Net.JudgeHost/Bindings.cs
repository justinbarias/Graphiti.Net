namespace Graphiti.JudgeHost;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// Maps each judge function name to the ported method. Each ported unit has its own file in <c>Units/</c>, and this
/// class collects them. A binding can do three things only: decode the arguments, call the product code, and encode
/// the result. It must not branch on the case, fill in defaults, or repair output. REVIEW.md checks this.
/// </summary>
/// <example>
/// A unit file is a static class in the namespace <c>Graphiti.JudgeHost.Units</c> whose name ends in <c>Bindings</c>,
/// with one method <c>Register</c>. The usual binding names the Python parameters in order and lets
/// <see cref="JudgeParameters.InvokeAsync"/> read, convert, and default each one. A constructor, a property, or a
/// method on an instance uses the explicit reads.
/// <code>
/// internal static class HelpersBindings
/// {
///     internal static void Register(BindingTable table)
///     {
///         table.Pure.Add("graphiti_core.helpers:lucene_sanitize", call => call.Signature("query").InvokeAsync(Helpers.LuceneSanitize));
///         table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeDuplicate", NodeDuplicate.ModelJsonSchema);
///     }
/// }
/// </code>
/// </example>
internal static class Bindings
{
    public const string NotBound = "JudgeNotBound";

    public const string TypeRejected = "JudgeTypeRejected";

    private const string UnitNamespace = "Graphiti.JudgeHost.Units";

    private static readonly BindingTable Table = Load(typeof(Bindings).Assembly.GetTypes());

    /// <summary>
    /// Gets the <c>$ref</c>, <c>$enum</c>, and <c>$model</c> paths and the encoders of product types.
    /// A unit registers its paths and its model types through <see cref="BindingTable.Registry"/>.
    /// </summary>
    internal static JudgeRegistry Registry => Table.Registry;

    /// <summary>
    /// Gets the JSON schema of each ported model, by the Python path of its class. The judge function
    /// <c>judge:schema_text</c> reads it.
    /// </summary>
    internal static Dictionary<string, PydanticSchema> Schemas => Table.Schemas;

    /// <summary>Gets the binding of each judge function name.</summary>
    internal static Dictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> Pure => Table.Pure;

    /// <summary>Runs a flow scenario. Not bound until the facade is ported (plan 0004, layer 9).</summary>
    public static ValueTask<JsonNode> RunFlowAsync(JsonElement scenario)
    {
        _ = scenario;
        return ValueTask.FromResult<JsonNode>(new JsonObject { ["host_error"] = NotBound });
    }

    /// <summary>
    /// Builds the table: the two judge functions, then <c>Register</c> of each unit class in name order.
    /// A unit class is a type of the unit namespace, or a type with a static method <c>Register(BindingTable)</c>.
    /// Each one must have all three: the namespace, a name that ends in <c>Bindings</c>, and the method. A class
    /// that lacks one, a <c>Register</c> that throws, and a name that two units bind stop the host at its start,
    /// so that a unit file never binds nothing in silence.
    /// </summary>
    internal static BindingTable Load(IEnumerable<Type> types)
    {
        var table = new BindingTable();
        table.Pure.Add("judge:embed", call => call.Signature("text").InvokeAsync(JudgeEmbedding.Embed));
        table.Pure.Add("judge:schema_text", call => call.Signature("model_path").InvokeAsync((Func<string, string>)(path => SchemaText(table, path))));

        // A nested type is a closure class that the compiler made.
        var units = types
            .Where(type => !type.IsNested && type != typeof(Bindings) && (type.Namespace == UnitNamespace || FindRegister(type) is not null))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
        foreach (var unit in units)
        {
            if (unit.Namespace != UnitNamespace || !unit.Name.EndsWith("Bindings", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{unit.FullName} is not a unit class: the namespace must be {UnitNamespace}, and the name must end in Bindings.");
            }

            var register = FindRegister(unit)
                ?? throw new InvalidOperationException($"{unit.FullName} has no static method Register(BindingTable).");
            try
            {
                register.Invoke(null, [table]);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                throw new InvalidOperationException($"{unit.FullName}.Register failed: {error.InnerException.Message}", error.InnerException);
            }
        }

        return table;
    }

    private static MethodInfo? FindRegister(Type type) =>
        type.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, [typeof(BindingTable)]);

    /// <summary>
    /// <c>json.dumps(M.model_json_schema())</c>, the text that upstream puts into a prompt, as <c>_schema_text</c> of
    /// <c>port/judge/host-python/host.py</c> gives it. A model that no unit added yet is not bound.
    /// </summary>
    private static string SchemaText(BindingTable table, string modelPath) =>
        table.Schemas.TryGetValue(modelPath, out var schema)
            ? PyJson.Dumps(schema.ToDict())
            : throw new JudgeNotBoundException($"No ported model has the path '{modelPath}'.");
}

/// <summary>What a unit fills in its <c>Register</c> method.</summary>
internal sealed class BindingTable
{
    /// <summary>Gets the binding of each judge function name. <c>Add</c> throws for a name that is bound already.</summary>
    internal Dictionary<string, Func<JudgeArguments, ValueTask<JsonNode?>>> Pure { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the JSON schema of each ported model, by the Python path of its class.</summary>
    internal Dictionary<string, PydanticSchema> Schemas { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the <c>$ref</c>, <c>$enum</c>, and <c>$model</c> paths and the encoders of product types.</summary>
    internal JudgeRegistry Registry { get; } = new();
}
