namespace Graphiti.JudgeHost.Core;

using System.Text;
using System.Text.Json;
using Neo4j.Driver;

/// <summary>The Python class name of each exception type of RULES.md R4 (open item O2).</summary>
internal static partial class Codec
{
    /// <summary>
    /// The built-in exceptions of the second table of R4, and the rows that the host had before. The lookup walks
    /// from the runtime type to its base types and takes the first type with a row. So a derived type always wins
    /// over its base, whatever the order of the rows: <c>ValidationException</c>, <c>DecoderFallbackException</c>,
    /// <c>EncoderFallbackException</c>, <c>ArgumentNullException</c>, and <c>ArgumentOutOfRangeException</c> before
    /// <c>ArgumentException</c>, and <c>TaskCanceledException</c> through <c>OperationCanceledException</c>.
    /// The rows are still written derived first. The key is the full type name, because
    /// <c>Graphiti.PyCompat.ValidationException</c> does not exist yet.
    /// </summary>
    internal static readonly IReadOnlyList<(string Type, string Python)> BuiltinExceptionNames =
    [
        ("Graphiti.PyCompat.ValidationException", "ValidationError"),
        (typeof(DecoderFallbackException).FullName!, "UnicodeDecodeError"),
        (typeof(EncoderFallbackException).FullName!, "UnicodeEncodeError"),
        (typeof(ArgumentNullException).FullName!, "TypeError"),
        (typeof(ArgumentOutOfRangeException).FullName!, "IndexError"),
        (typeof(ArgumentException).FullName!, "ValueError"),
        (typeof(JsonException).FullName!, "JSONDecodeError"),
        (typeof(FormatException).FullName!, "SyntaxError"),
        (typeof(InvalidCastException).FullName!, "TypeError"),
        (typeof(KeyNotFoundException).FullName!, "KeyError"),
        (typeof(IndexOutOfRangeException).FullName!, "IndexError"),
        (typeof(MissingMemberException).FullName!, "AttributeError"),
        (typeof(NotImplementedException).FullName!, "NotImplementedError"),
        (typeof(InvalidOperationException).FullName!, "RuntimeError"),
        (typeof(DivideByZeroException).FullName!, "ZeroDivisionError"),
        (typeof(OverflowException).FullName!, "OverflowError"),
        (typeof(TaskCanceledException).FullName!, "CancelledError"),
        (typeof(OperationCanceledException).FullName!, "CancelledError"),
        (typeof(TimeoutException).FullName!, "TimeoutError"),
    ];

    /// <summary>
    /// The ported exception classes of <c>errors.py</c> and <c>llm_client/errors.py</c> (the first table of R4), by
    /// C# type name. A row applies to a type in the namespace <c>Graphiti</c> or below it. Derived types come first.
    /// </summary>
    internal static readonly IReadOnlyList<(string Type, string Python)> GraphitiExceptionNames =
    [
        ("EdgeNotFoundException", "EdgeNotFoundError"),
        ("EdgesNotFoundException", "EdgesNotFoundError"),
        ("GroupsEdgesNotFoundException", "GroupsEdgesNotFoundError"),
        ("GroupsNodesNotFoundException", "GroupsNodesNotFoundError"),
        ("NodeNotFoundException", "NodeNotFoundError"),
        ("SearchRerankerException", "SearchRerankerError"),
        ("EntityTypeValidationException", "EntityTypeValidationError"),
        ("IdentityPropertyValidationException", "IdentityPropertyValidationError"),
        ("GroupIdValidationException", "GroupIdValidationError"),
        ("UnitExpiredException", "UnitExpiredError"),
        ("UnitInProgressException", "UnitInProgressError"),
        ("StaleCommitException", "StaleCommitError"),
        ("NodeLabelValidationException", "NodeLabelValidationError"),
        ("ChangeSetRejectedException", "ChangeSetRejected"),
        ("GraphitiException", "GraphitiError"),
        ("RateLimitException", "RateLimitError"),
        ("RefusalException", "RefusalError"),
        ("EmptyResponseException", "EmptyResponseError"),
        ("OpenAIDecisionsInputTooLongException", "OpenAIDecisionsInputTooLongError"),
        ("OpenAIInputTooLongSignalException", "_OpenAIInputTooLongSignal"),
    ];

    /// <summary>
    /// The Python driver classes for Neo4j status codes, as <c>neo4j.exceptions</c> 5.x maps them. A code with no row
    /// takes the class of its classification: <c>ClientError</c>, <c>TransientError</c>, or <c>DatabaseError</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Neo4jCodeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Neo.ClientError.Statement.SyntaxError"] = "CypherSyntaxError",
        ["Neo.ClientError.Statement.InvalidSyntax"] = "CypherSyntaxError",
        ["Neo.ClientError.Statement.TypeError"] = "CypherTypeError",
        ["Neo.ClientError.Statement.InvalidType"] = "CypherTypeError",
        ["Neo.ClientError.Procedure.TypeError"] = "CypherTypeError",
        ["Neo.ClientError.Schema.ConstraintValidationFailed"] = "ConstraintError",
        ["Neo.ClientError.Schema.ConstraintViolation"] = "ConstraintError",
        ["Neo.ClientError.Statement.ConstraintVerificationFailed"] = "ConstraintError",
        ["Neo.ClientError.Statement.ConstraintViolation"] = "ConstraintError",
        ["Neo.ClientError.Security.Unauthorized"] = "AuthError",
        ["Neo.ClientError.Security.Forbidden"] = "Forbidden",
        ["Neo.ClientError.Security.TokenExpired"] = "TokenExpired",
        ["Neo.ClientError.Cluster.NotALeader"] = "NotALeader",
        ["Neo.ClientError.General.ForbiddenOnReadOnlyDatabase"] = "ForbiddenOnReadOnlyDatabase",
        ["Neo.TransientError.Transaction.Terminated"] = "ClientError",
        ["Neo.TransientError.Transaction.LockClientStopped"] = "ClientError",
    };

    private static readonly Dictionary<string, string> BuiltinByType =
        BuiltinExceptionNames.ToDictionary(row => row.Type, row => row.Python, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> GraphitiByType =
        GraphitiExceptionNames.ToDictionary(row => row.Type, row => row.Python, StringComparer.Ordinal);

    /// <summary>
    /// The Python class name that a .NET exception stands for (RULES.md R4, open item O2). An instance can say more
    /// than its type: the status code of an <see cref="HttpRequestException"/> and the code of a Neo4j error.
    /// A type with no row gives its own C# name.
    /// </summary>
    internal static string PythonExceptionName(Exception error) => error switch
    {
        Neo4jException { Code: { Length: > 0 } code } => Neo4jName(code),
        HttpRequestException { StatusCode: not null } => "HTTPStatusError",
        HttpRequestException => "TransportError",
        _ => PythonExceptionName(error.GetType()),
    };

    /// <summary>The Python class name of an exception type, for a <c>$class</c> result and for an error.</summary>
    internal static string PythonExceptionName(Type type)
    {
        for (var current = type; current is not null && current != typeof(Exception); current = current.BaseType)
        {
            if (BuiltinByType.TryGetValue(current.FullName ?? current.Name, out var builtin))
            {
                return builtin;
            }

            if (IsGraphitiNamespace(current.Namespace) && GraphitiByType.TryGetValue(current.Name, out var ported))
            {
                return ported;
            }

            if (current == typeof(ServiceUnavailableException))
            {
                return "ServiceUnavailable";
            }

            if (current == typeof(SessionExpiredException))
            {
                return "SessionExpired";
            }

            if (current == typeof(ResultConsumedException))
            {
                return "ResultConsumedError";
            }

            if (current == typeof(ClientException))
            {
                return "ClientError";
            }

            if (current == typeof(TransientException))
            {
                return "TransientError";
            }

            if (current == typeof(DatabaseException))
            {
                return "DatabaseError";
            }

            if (current == typeof(Neo4jException))
            {
                return "Neo4jError";
            }
        }

        return type.Name;
    }

    private static bool IsGraphitiNamespace(string? ns) =>
        ns is not null && (ns == "Graphiti" || ns.StartsWith("Graphiti.", StringComparison.Ordinal));

    private static string Neo4jName(string code)
    {
        if (Neo4jCodeNames.TryGetValue(code, out var name))
        {
            return name;
        }

        return code.StartsWith("Neo.ClientError.", StringComparison.Ordinal) ? "ClientError"
            : code.StartsWith("Neo.TransientError.", StringComparison.Ordinal) ? "TransientError"
            : code.StartsWith("Neo.DatabaseError.", StringComparison.Ordinal) ? "DatabaseError"
            : "Neo4jError";
    }
}
