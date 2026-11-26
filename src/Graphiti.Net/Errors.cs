// Ported from graphiti_core/errors.py
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

namespace Graphiti;

using Graphiti.PyCompat;

/// <summary>Base exception class for Graphiti Core.</summary>
public class GraphitiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GraphitiException"/> class.</summary>
    public GraphitiException(string message = "")
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphitiException"/> class with the exception that caused this one.</summary>
    public GraphitiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an edge is not found.</summary>
public class EdgeNotFoundException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="EdgeNotFoundException"/> class.</summary>
    public EdgeNotFoundException(string uuid)
        : this(uuid, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EdgeNotFoundException"/> class with the exception that caused this one.</summary>
    public EdgeNotFoundException(string uuid, Exception? innerException)
        : base($"edge {uuid} not found", innerException)
    {
    }
}

/// <summary>Raised when a list of edges is not found.</summary>
public class EdgesNotFoundException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="EdgesNotFoundException"/> class.</summary>
    public EdgesNotFoundException(List<string> uuids)
        : this(uuids, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EdgesNotFoundException"/> class with the exception that caused this one.</summary>
    public EdgesNotFoundException(List<string> uuids, Exception? innerException)
        : base($"None of the edges for {Py.Repr(uuids)} were found.", innerException)
    {
    }
}

/// <summary>Raised when no edges are found for a list of group ids.</summary>
public class GroupsEdgesNotFoundException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="GroupsEdgesNotFoundException"/> class.</summary>
    public GroupsEdgesNotFoundException(List<string> groupIds)
        : this(groupIds, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GroupsEdgesNotFoundException"/> class with the exception that caused this one.</summary>
    public GroupsEdgesNotFoundException(List<string> groupIds, Exception? innerException)
        : base($"no edges found for group ids {Py.Repr(groupIds)}", innerException)
    {
    }
}

/// <summary>Raised when no nodes are found for a list of group ids.</summary>
public class GroupsNodesNotFoundException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="GroupsNodesNotFoundException"/> class.</summary>
    public GroupsNodesNotFoundException(List<string> groupIds)
        : this(groupIds, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GroupsNodesNotFoundException"/> class with the exception that caused this one.</summary>
    public GroupsNodesNotFoundException(List<string> groupIds, Exception? innerException)
        : base($"no nodes found for group ids {Py.Repr(groupIds)}", innerException)
    {
    }
}

/// <summary>Raised when a node is not found.</summary>
public class NodeNotFoundException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="NodeNotFoundException"/> class.</summary>
    public NodeNotFoundException(string uuid)
        : this(uuid, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NodeNotFoundException"/> class with the exception that caused this one.</summary>
    public NodeNotFoundException(string uuid, Exception? innerException)
        : base($"node {uuid} not found", innerException)
    {
    }
}

/// <summary>Raised when a node is not found.</summary>
public class SearchRerankerException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="SearchRerankerException"/> class.</summary>
    public SearchRerankerException(string text)
        : this(text, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SearchRerankerException"/> class with the exception that caused this one.</summary>
    public SearchRerankerException(string text, Exception? innerException)
        : base(text, innerException)
    {
    }
}

/// <summary>Raised when an entity type uses protected attribute names.</summary>
public class EntityTypeValidationException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="EntityTypeValidationException"/> class.</summary>
    public EntityTypeValidationException(string entityType, string entityTypeAttribute)
        : this(entityType, entityTypeAttribute, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EntityTypeValidationException"/> class with the exception that caused this one.</summary>
    public EntityTypeValidationException(string entityType, string entityTypeAttribute, Exception? innerException)
        : base($"{entityTypeAttribute} cannot be used as an attribute for {entityType} as it is a protected attribute name.", innerException)
    {
    }
}

/// <summary>Raised when identity properties do not match the configured entity types.</summary>
public class IdentityPropertyValidationException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="IdentityPropertyValidationException"/> class.</summary>
    public IdentityPropertyValidationException(string entityType, string reason)
        : this(entityType, reason, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IdentityPropertyValidationException"/> class with the exception that caused this one.</summary>
    public IdentityPropertyValidationException(string entityType, string reason, Exception? innerException)
        : base($"Invalid identity properties for {entityType}: {reason}", innerException)
    {
    }
}

/// <summary>Raised when a group_id contains invalid characters.</summary>
public class GroupIdValidationException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="GroupIdValidationException"/> class.</summary>
    public GroupIdValidationException(string groupId)
        : this(groupId, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GroupIdValidationException"/> class with the exception that caused this one.</summary>
    public GroupIdValidationException(string groupId, Exception? innerException)
        : base($"group_id \"{groupId}\" must contain only alphanumeric characters, dashes, or underscores", innerException)
    {
    }
}

/// <summary>Raised when a node label contains invalid characters.</summary>
// PORT: R4. Python also derives from ValueError, and C# has one base class. Pydantic still catches the
// exception as a ValueError when a field validator raises it. Rule R7 covers that with
// Pydantic.Wrap<NodeLabelValidationException> at the two validator sites.
public class NodeLabelValidationException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="NodeLabelValidationException"/> class.</summary>
    public NodeLabelValidationException(List<string> nodeLabels)
        : this(nodeLabels, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NodeLabelValidationException"/> class with the exception that caused this one.</summary>
    public NodeLabelValidationException(List<string> nodeLabels, Exception? innerException)
        : base(BuildMessage(nodeLabels), innerException)
    {
    }

    // PORT: Python builds the message in the body of __init__. C# needs it before the base constructor call.
    private static string BuildMessage(List<string> nodeLabels)
    {
        var labelList = string.Join(", ", nodeLabels.Select(label => $"\"{label}\""));
        return "node_labels must start with a letter or underscore and contain only "
            + $"alphanumeric characters or underscores: {labelList}";
    }
}

/// <summary>Raised when an ingestion unit calls the state store after its deadline or after it failed.</summary>
public class UnitExpiredException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="UnitExpiredException"/> class.</summary>
    public UnitExpiredException(string groupId, long seq)
        : this(groupId, seq, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnitExpiredException"/> class with the exception that caused this one.</summary>
    public UnitExpiredException(string groupId, long seq, Exception? innerException)
        : base($"ingestion unit {Py.Str(seq)} of group {groupId} is expired or failed", innerException)
    {
    }
}

/// <summary>
/// Raised when an admission names the <c>unit_id</c> of an ingestion unit that is in progress.
/// <para>
/// The admission writes nothing. The host can try again after the lease time of the unit.
/// </para>
/// </summary>
public class UnitInProgressException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="UnitInProgressException"/> class.</summary>
    public UnitInProgressException(string groupId, string unitId, long seq)
        : this(groupId, unitId, seq, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnitInProgressException"/> class with the exception that caused this one.</summary>
    public UnitInProgressException(string groupId, string unitId, long seq, Exception? innerException)
        : base($"ingestion unit {Py.Str(seq)} of group {groupId} with the unit_id {unitId} is in progress", innerException)
    {
        GroupId = groupId;
        UnitId = unitId;
        Seq = seq;
    }

    /// <summary>Gets the Python attribute <c>group_id</c>.</summary>
    public string GroupId { get; }

    /// <summary>Gets the Python attribute <c>unit_id</c>.</summary>
    public string UnitId { get; }

    /// <summary>Gets the Python attribute <c>seq</c>.</summary>
    public long Seq { get; }
}

/// <summary>Raised when a commit attempt uses a fence that is not the current fence of the unit.</summary>
public class StaleCommitException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="StaleCommitException"/> class.</summary>
    public StaleCommitException(string groupId, long seq)
        : this(groupId, seq, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StaleCommitException"/> class with the exception that caused this one.</summary>
    public StaleCommitException(string groupId, long seq, Exception? innerException)
        : base($"commit attempt for ingestion unit {Py.Str(seq)} of group {groupId} is stale", innerException)
    {
    }
}

/// <summary>Raised by a host hook to reject the change set of an ingestion unit before the write.</summary>
public class ChangeSetRejectedException : GraphitiException
{
    /// <summary>Initializes a new instance of the <see cref="ChangeSetRejectedException"/> class.</summary>
    public ChangeSetRejectedException(string reason = "change set rejected")
        : this(reason, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ChangeSetRejectedException"/> class with the exception that caused this one.</summary>
    public ChangeSetRejectedException(string reason, Exception? innerException)
        : base(reason, innerException)
    {
    }
}
