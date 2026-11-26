// Ported from graphiti_core/prompts/models.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Prompts;

using Graphiti.Internal;
using Graphiti.PyCompat;

/// <summary>LLM transport message (role + content). Used by LLMClient.generate_response.</summary>
internal sealed record Message : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("Message", "LLM transport message (role + content). Used by LLMClient.generate_response.")
        .Required("role", PydanticType.Str)
        .Required("content", PydanticType.Str);

    internal required string Role { get; set; }

    internal required string Content { get; set; }

    internal Message Validate() => this;

    internal static Message ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Message", data);
        var role = f.Required("role", Pydantic.Str);
        var content = f.Required("content", Pydantic.Str);
        f.ThrowIfAny();
        return new Message { Role = role.Value, Content = content.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("role", Role), ("content", Content));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

internal delegate List<Message> PromptVersion(OrderedDictionary<string, object?> context);

internal delegate List<Message> PromptFunction(OrderedDictionary<string, object?> context);

internal sealed record SystemMessage : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SystemMessage")
        .Default("role", PydanticType.Literal("system"), "system")
        .Required("content", PydanticType.Str);

    internal string Role { get; set; } = "system";

    internal required string Content { get; set; }

    internal SystemMessage Validate()
    {
        Pydantic.Literal(Role, "system");
        return this;
    }

    internal static SystemMessage ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SystemMessage", data);
        var role = f.Default("role", v => Pydantic.Literal(v, "system"), () => "system");
        var content = f.Required("content", Pydantic.Str);
        f.ThrowIfAny();
        return new SystemMessage { Role = role.Value, Content = content.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("role", Role), ("content", Content));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

internal sealed record UserMessage : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("UserMessage")
        .Default("role", PydanticType.Literal("user"), "user")
        .Required("content", PydanticType.Str);

    internal string Role { get; set; } = "user";

    internal required string Content { get; set; }

    internal UserMessage Validate()
    {
        Pydantic.Literal(Role, "user");
        return this;
    }

    internal static UserMessage ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("UserMessage", data);
        var role = f.Default("role", v => Pydantic.Literal(v, "user"), () => "user");
        var content = f.Required("content", Pydantic.Str);
        f.ThrowIfAny();
        return new UserMessage { Role = role.Value, Content = content.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("role", Role), ("content", Content));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Typed system+user prompt returned by all prompt builders.</summary>
internal sealed record ChatPrompt : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("ChatPrompt", "Typed system+user prompt returned by all prompt builders.")
        .Required("system", PydanticType.Model(SystemMessage.ModelJsonSchema))
        .Required("user", PydanticType.Model(UserMessage.ModelJsonSchema));

    internal required SystemMessage System { get; set; }

    internal required UserMessage User { get; set; }

    internal static ChatPrompt FromMessages(List<Message> messages)
    {
        if (messages.Count != 2)
        {
            throw new ArgumentException("ChatPrompt requires exactly two messages");
        }

        if (messages[0].Role != "system" || messages[1].Role != "user")
        {
            throw new ArgumentException("ChatPrompt requires a system message followed by a user message");
        }

        return new ChatPrompt
        {
            System = new SystemMessage { Content = messages[0].Content }.Validate(),
            User = new UserMessage { Content = messages[1].Content }.Validate(),
        }.Validate();
    }

    internal ChatPrompt Validate() => this;

    internal static ChatPrompt ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ChatPrompt", data);
        var system = f.Required("system", v => Pydantic.Model(v, "SystemMessage", SystemMessage.ModelValidate));
        var user = f.Required("user", v => Pydantic.Model(v, "UserMessage", UserMessage.ModelValidate));
        f.ThrowIfAny();
        return new ChatPrompt { System = system.Value, User = user.Value }.Validate();
    }

    /// <summary>
    /// Render to transport messages.
    ///
    /// When ``append_unicode_note`` is True (default), appends the do-not-escape-unicode
    /// note to the system message content so LLM providers preserve non-ASCII characters.
    /// </summary>
    /// <param name="appendUnicodeNote">Append the do-not-escape-unicode note to the system message.</param>
    /// <returns>The transport messages.</returns>
    internal List<Message> AsMessages(bool appendUnicodeNote = true)
    {
        var systemContent = System.Content;
        if (appendUnicodeNote)
        {
            systemContent = systemContent + PromptHelpers.DoNotEscapeUnicode;
        }

        return
        [
            new Message { Role = "system", Content = systemContent }.Validate(),
            new Message { Role = "user", Content = User.Content }.Validate(),
        ];
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("system", System), ("user", User));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>
/// Fixed schema registry entry for a production prompt.
///
/// Schemas are not overridable — only text builders may be customized.
/// </summary>
internal sealed record PromptSpec : IPydanticModel
{
    internal required string Name { get; init; }

    internal IResponseModel? ResponseModel { get; init; }

    internal bool DynamicSchema { get; init; }

    internal PromptSpec Validate() => this;

    internal static PromptSpec ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("PromptSpec", data);
        var name = f.Required("name", Pydantic.Str);
        var responseModel = f.Default("response_model", v => Pydantic.Optional(v, ReadResponseModel), () => null);
        var dynamicSchema = f.Default("dynamic_schema", Pydantic.Bool, () => false);
        f.ThrowIfAny();
        return new PromptSpec { Name = name.Value, ResponseModel = responseModel.Value, DynamicSchema = dynamicSchema.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("name", Name), ("response_model", ResponseModel), ("dynamic_schema", DynamicSchema));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    // PORT: pydantic checks `type[BaseModel]` with is_subclass_of. An IResponseModel stands for a model class (R7).
    private static IResponseModel ReadResponseModel(object? value) =>
        value as IResponseModel ?? throw ValidationException.One("is_subclass_of", "Input should be a subclass of BaseModel");
}

internal delegate ChatPrompt ChatPromptFunction(OrderedDictionary<string, object?> context);
