namespace Graphiti;

/// <summary>
/// The connection of the Neo4j driver that <c>AddNeo4j</c> builds: the parameters of upstream's <c>Neo4jDriver</c>.
/// Graphiti closes the driver when the container disposes Graphiti.
/// </summary>
public sealed class Neo4jOptions
{
    /// <summary>Gets or sets the URI of the database, for example <c>bolt://localhost:7687</c>. It must have a value.</summary>
    public string? Uri { get; set; }

    /// <summary>Gets or sets the user name, or null for an empty name.</summary>
    public string? User { get; set; }

    /// <summary>Gets or sets the password, or null for an empty password.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the name of the database. The default is <c>neo4j</c>.</summary>
    public string Database { get; set; } = "neo4j";
}
