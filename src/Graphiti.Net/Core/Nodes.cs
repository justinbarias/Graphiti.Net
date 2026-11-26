// Ported from graphiti_core/nodes.py
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

namespace Graphiti.Core;

using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class Nodes
{
    // PORT: R5 puts the logger on the module class. EpisodeTypeExtensions is a separate class (R7), so the logger is internal.
    internal static ILogger Logger => PortServices.Logger(typeof(Nodes));
}

/// <summary>
/// Enumeration of different types of episodes that can be processed.
///
/// This enum defines the various sources or formats of episodes that the system
/// can handle. It's used to categorize and potentially handle different types
/// of input data differently.
///
/// Attributes:
/// -----------
/// message : str
///     Represents a standard message-type episode. The content for this type
///     should be formatted as "actor: content". For example, "user: Hello, how are you?"
///     or "assistant: I'm doing well, thank you for asking."
/// json : str
///     Represents an episode containing a JSON string object with structured data.
/// text : str
///     Represents a plain text episode.
/// </summary>
internal enum EpisodeType
{
    [PyEnumMember("message", "message")]
    Message,

    [PyEnumMember("json", "json")]
    Json,

    [PyEnumMember("text", "text")]
    Text,

    [PyEnumMember("fact_triple", "fact_triple")]
    FactTriple,
}

/// <summary>The static methods of the Python class <c>EpisodeType</c> (rule R7).</summary>
internal static class EpisodeTypeExtensions
{
    internal static EpisodeType FromStr(string episodeType)
    {
        if (episodeType == "message")
        {
            return EpisodeType.Message;
        }

        if (episodeType == "json")
        {
            return EpisodeType.Json;
        }

        if (episodeType == "text")
        {
            return EpisodeType.Text;
        }

        if (episodeType == "fact_triple")
        {
            return EpisodeType.FactTriple;
        }

        Nodes.Log.EpisodeTypeNotImplemented(Nodes.Logger, episodeType);
        throw new NotImplementedException(string.Empty);
    }
}

internal abstract class Node
{
    private List<string> labels = [];

    internal string Uuid { get; set; } = Py.Uuid4();

    internal required string Name { get; set; }

    internal required string GroupId { get; set; }

    // PORT: ConfigDict(validate_assignment=True) with a validator on this field, so the set accessor validates (R7).
    internal List<string> Labels
    {
        get => labels;
        set => labels = ValidateLabels(value, "labels");
    }

    internal PyDateTime CreatedAt { get; set; } = DatetimeUtils.UtcNow();

    // PORT: the pydantic error carries the name of the class of the instance (R15).
    internal abstract string PythonName { get; }

    internal List<string> ValidateLabels(List<string> value, string? field = null)
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => Helpers.ValidateNodeLabels(value), PythonName, field);
        return value;
    }

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Node other && Uuid == other.Uuid;
}

// PORT: a shell that packet S1 declares (plan 0004, layer 0). Packet S2 fills it. PythonName is the one member,
// because the abstract base Node needs it (R7).
internal sealed class EpisodicNode : Node
{
    internal override string PythonName => "EpisodicNode";
}

internal sealed class EntityNode : Node
{
    internal List<double>? NameEmbedding { get; set; }

    internal string Summary { get; set; } = string.Empty;

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    internal override string PythonName => "EntityNode";

    internal EntityNode Validate()
    {
        ValidateLabels(Labels, "labels");
        return this;
    }
}

// PORT: a shell that packet S1 declares (plan 0004, layer 0). Packet S2 fills it. PythonName is the one member,
// because the abstract base Node needs it (R7).
internal sealed class CommunityNode : Node
{
    internal override string PythonName => "CommunityNode";
}

// PORT: a shell that packet S1 declares (plan 0004, layer 0). Packet S2 fills it. PythonName is the one member,
// because the abstract base Node needs it (R7).
internal sealed class SagaNode : Node
{
    internal override string PythonName => "SagaNode";
}

internal static partial class Nodes
{
    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Episode type: {EpisodeType} not implemented")]
        internal static partial void EpisodeTypeNotImplemented(ILogger logger, string episodeType);
    }
}
