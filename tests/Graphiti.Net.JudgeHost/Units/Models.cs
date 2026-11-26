namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/models.py</c>.</summary>
internal static class ModelsBindings
{
    internal static void Register(BindingTable table)
    {
        // A model class called with keyword arguments only: M(**kwargs) is M.ModelValidate(kwargs) (R7).
        table.Pure.Add("graphiti_core.prompts.models:Message", call => Codec.Result(Message.ModelValidate(Keywords(call))));
        table.Pure.Add("graphiti_core.prompts.models:ChatPrompt", call => Codec.Result(ChatPrompt.ModelValidate(Keywords(call))));
        table.Pure.Add("graphiti_core.prompts.models:PromptSpec", call => Codec.Result(PromptSpec.ModelValidate(Keywords(call))));

        table.Pure.Add("graphiti_core.prompts.models:ChatPrompt.from_messages", call => call.Signature("messages").InvokeAsync(ChatPrompt.FromMessages));
        table.Pure.Add("graphiti_core.prompts.models:ChatPrompt.as_messages", call =>
        {
            var p = call.Signature("self", "append_unicode_note");
            var self = p.Model<ChatPrompt>("self");
            return Codec.Result(p.Has("append_unicode_note") ? self.AsMessages(p.Bool("append_unicode_note")) : self.AsMessages());
        });

        table.Schemas.Add("graphiti_core.prompts.models:Message", Message.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.models:SystemMessage", SystemMessage.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.models:UserMessage", UserMessage.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.models:ChatPrompt", ChatPrompt.ModelJsonSchema);

        table.Registry
            .AddModel("graphiti_core.prompts.models:Message", data => Message.ModelValidate(data))
            .AddModel("graphiti_core.prompts.models:ChatPrompt", data => ChatPrompt.ModelValidate(data))
            .AddType<Message>("Message", m => [new("role", m.Role), new("content", m.Content)])
            .AddType<SystemMessage>("SystemMessage", m => [new("role", m.Role), new("content", m.Content)])
            .AddType<UserMessage>("UserMessage", m => [new("role", m.Role), new("content", m.Content)])
            .AddType<ChatPrompt>("ChatPrompt", m => [new("system", m.System), new("user", m.User)])
            .AddType<PromptSpec>("PromptSpec", m => [new("name", m.Name), new("response_model", m.ResponseModel), new("dynamic_schema", m.DynamicSchema)])
            .AddEncoder<IResponseModel>(m => Codec.Class(m.PythonName));
    }

    // A pydantic model takes no positional argument: Python raises TypeError.
    private static OrderedDictionary<string, object?> Keywords(JudgeArguments call) =>
        call.Positional.Count == 0
            ? call.Keywords
            : throw new JudgeTypeRejectedException($"takes 1 positional argument but {call.Positional.Count + 1} were given");
}
