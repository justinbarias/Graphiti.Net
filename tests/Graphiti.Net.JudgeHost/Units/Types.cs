namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>
/// The path <c>types:SimpleNamespace</c>, which judge cases pass as a duck-typed object. This file is lead code,
/// because cases of several units share the path (RULES.md R22). Three <c>validate_prompt_library</c> cases pass a
/// namespace as a prompt library. The <c>fulltext_query</c> cases pass one as a driver, and the unit of that module
/// extends <see cref="Namespace"/> with the lead.
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
}
