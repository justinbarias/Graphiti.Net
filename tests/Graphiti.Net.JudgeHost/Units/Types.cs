namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Driver;
using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

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
    /// A <c>SimpleNamespace</c> as a <c>GraphDriver</c>: the driver that a <c>fulltext_query</c> case passes. It gives the
    /// <c>provider</c> and <c>fulltext_syntax</c> attributes that the namespace has, and <c>build_fulltext_query</c>
    /// reads the namespace at the call. <c>Provider</c> and <c>FulltextSyntax</c> are plain properties of
    /// <c>GraphDriver</c>, so a read of an attribute that the namespace lacks cannot throw at the read itself. The
    /// binding calls <see cref="ThrowIfAttributeRead"/> after the call and gets the <c>AttributeError</c> of Python at
    /// the place where Python reads the attribute: after the validation of the group ids, and never at the bind.
    /// </summary>
    internal sealed class NamespaceDriver : GraphDriver
    {
        private readonly Namespace source;

        internal NamespaceDriver(Namespace source)
        {
            this.source = source;
            Provider = source.Attributes.TryGetValue("provider", out var provider)
                ? (GraphProvider)JudgeTypes.Convert(typeof(GraphProvider), provider, "attribute 'provider'")!
                : GraphProvider.Neo4j;
            FulltextSyntax = source.Attributes.TryGetValue("fulltext_syntax", out var syntax)
                ? (string)JudgeTypes.Convert(typeof(string), syntax, "attribute 'fulltext_syntax'")!
                : string.Empty;
        }

        internal override string PythonName => "SimpleNamespace";

        /// <summary>
        /// Throws the <c>AttributeError</c> of the first attribute that <c>fulltext_query</c> reads and the namespace
        /// lacks. Python reads <c>driver.provider</c> after the validation of the group ids. For another provider than
        /// FalkorDB it reads <c>driver.fulltext_syntax</c> once for each group id.
        /// </summary>
        internal void ThrowIfAttributeRead(List<string>? groupIds)
        {
            if (!source.Attributes.ContainsKey("provider"))
            {
                throw Missing("provider");
            }

            if (Provider != GraphProvider.Falkordb && groupIds is { Count: > 0 } && !source.Attributes.ContainsKey("fulltext_syntax"))
            {
                throw Missing("fulltext_syntax");
            }
        }

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
