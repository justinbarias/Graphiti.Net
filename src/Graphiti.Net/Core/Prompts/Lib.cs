// Ported from graphiti_core/prompts/lib.py (the two interfaces of R15 only)
namespace Graphiti.Core.Prompts;

internal interface IPromptGroup
{
    bool TryGetFunction(string name, out ChatPromptFunction? function);
}

internal interface IPromptLibrary
{
    IReadOnlyDictionary<string, PromptSpec>? Specs { get; }

    bool TryGetGroup(string name, out IPromptGroup? group);
}
