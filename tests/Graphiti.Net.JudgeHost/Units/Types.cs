namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Driver;
using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// The path <c>types:SimpleNamespace</c>, which judge cases pass as a duck-typed object. This file is lead code,
/// because cases of several units share the path (RULES.md R22). Three <c>validate_prompt_library</c> cases pass a
/// namespace as a prompt library. The <c>fulltext_query</c> cases pass one as a driver: <see cref="NamespaceDriver"/>
/// is the form of a namespace for a <c>GraphDriver</c> parameter.
/// </summary>
internal static class TypesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Registry.AddModel("types:SimpleNamespace", data => new Namespace(data));
    }

    /// <summary>
    /// The <c>library</c> argument of <c>validate_prompt_library</c>. A library, a namespace or the default library,
    /// is itself. Each other value (a dict, a list, <c>None</c>, text, a number, a bool, a class) has no attribute with
    /// the name of a prompt group (<c>PROMPT_GROUPS</c>), because Python reads the groups with <c>hasattr</c>. So it is a
    /// library with no group, and Python raises <c>ValueError</c> for the first group, as it does for an empty namespace.
    /// </summary>
    internal static IPromptLibrary AsPromptLibrary(object? value) =>
        value as IPromptLibrary ?? new Namespace([]);

    /// <summary>
    /// A <c>SimpleNamespace</c>: one attribute for each key of the data. <c>hasattr</c> is the result of a
    /// <c>TryGet</c> member, and an attribute that has another type than the member asks for gives <c>true</c> with a
    /// null value, as R15 says for a value that is not callable.
    /// </summary>
    internal sealed class Namespace(OrderedDictionary<string, object?> attributes) : IPromptLibrary, IPromptGroup
    {
        internal OrderedDictionary<string, object?> Attributes { get; } = attributes;

        public IReadOnlyDictionary<string, PromptSpec>? Specs => Attributes.TryGetValue("specs", out var specs)
            ? specs as IReadOnlyDictionary<string, PromptSpec>
                ?? throw new JudgeHostErrorException("types:SimpleNamespace is bound for a specs attribute that is a mapping of PromptSpec only.")
            : null;

        public bool TryGetGroup(string name, out IPromptGroup? group)
        {
            var found = Attributes.TryGetValue(name, out var value);
            group = value as IPromptGroup;
            return found;
        }

        public bool TryGetFunction(string name, out ChatPromptFunction? function)
        {
            var found = Attributes.TryGetValue(name, out var value);
            function = value as ChatPromptFunction;
            return found;
        }
    }

    /// <summary>
    /// A <c>SimpleNamespace</c> as a <c>GraphDriver</c>: the driver that a <c>fulltext_query</c> case passes. It sets
    /// <c>Provider</c> and <c>FulltextSyntax</c> from the attributes that the namespace has, and it sets nothing for an
    /// attribute that the namespace lacks. Both are plain properties of <c>GraphDriver</c>, so the port cannot raise
    /// the <c>AttributeError</c> of Python for a missing attribute, and the binding does not raise it for the port. One
    /// case passes an empty namespace. It expects the error of the group id validation, which the port raises before
    /// it reads the driver.
    /// </summary>
    internal sealed class NamespaceDriver : GraphDriver
    {
        private readonly Namespace source;

        internal NamespaceDriver(Namespace source)
        {
            this.source = source;
            if (source.Attributes.TryGetValue("provider", out var provider))
            {
                Provider = (GraphProvider)JudgeTypes.Convert(typeof(GraphProvider), provider, "attribute 'provider'")!;
            }

            if (source.Attributes.TryGetValue("fulltext_syntax", out var syntax))
            {
                FulltextSyntax = (string)JudgeTypes.Convert(typeof(string), syntax, "attribute 'fulltext_syntax'")!;
            }
        }

        internal override string PythonName => "SimpleNamespace";

        internal override string BuildFulltextQuery(string query, List<string>? groupIds = null, long maxQueryLength = 128) =>
            throw (source.Attributes.ContainsKey("build_fulltext_query")
                ? new JudgeHostErrorException("types:SimpleNamespace is bound for a build_fulltext_query attribute that is a function of no case.")
                : Missing("build_fulltext_query"));

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw Missing("execute_query");

        internal override GraphDriverSession Session(string? database = null) => throw Missing("session");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw Missing("close");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw Missing("delete_all_indexes");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw Missing("build_indices_and_constraints");

        private static MissingMemberException Missing(string attribute) =>
            new($"'types.SimpleNamespace' object has no attribute '{attribute}'");
    }
}
