namespace Microsoft.Extensions.DependencyInjection;

using Graphiti;
using Graphiti.Internal;
using Graphiti.Neo4j;
using global::Neo4j.Driver;
using Microsoft.Extensions.Options;

/// <summary>The registration of the Neo4j driver of Graphiti.</summary>
public static class GraphitiNeo4jBuilderExtensions
{
    /// <summary>
    /// Gives Graphiti a Neo4j driver that it builds from <see cref="Neo4jOptions"/>. Graphiti closes the driver when the
    /// container disposes Graphiti. Options with no <see cref="Neo4jOptions.Uri"/> fail at the first resolution of
    /// <c>IGraphiti</c>, and at startup under the generic host.
    /// </summary>
    /// <param name="builder">The builder of <c>AddGraphiti</c>.</param>
    /// <param name="configure">An action that sets the options.</param>
    /// <returns>The builder.</returns>
    public static IGraphitiBuilder AddNeo4j(this IGraphitiBuilder builder, Action<Neo4jOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var registration = GraphitiRegistration.Of(builder);
        builder.Services.AddOptions<Neo4jOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Uri), "Neo4jOptions.Uri must have a value.")
            .ValidateOnStart();
        registration.Driver = services =>
        {
            var options = services.GetRequiredService<IOptions<Neo4jOptions>>().Value;
            return new Neo4jDriver(options.Uri!, options.User, options.Password, options.Database);
        };
        return builder;
    }

    /// <summary>
    /// Gives Graphiti a Neo4j driver on an <see cref="IDriver"/> that the host builds, with the database <c>neo4j</c>.
    /// Graphiti closes the <see cref="IDriver"/> when the container disposes Graphiti, as upstream's <c>close</c> closes
    /// the driver that it holds. A host that needs the <see cref="IDriver"/> after that gives Graphiti its own object.
    /// </summary>
    /// <param name="builder">The builder of <c>AddGraphiti</c>.</param>
    /// <param name="factory">A function that returns the driver.</param>
    /// <returns>The builder.</returns>
    public static IGraphitiBuilder AddNeo4j(this IGraphitiBuilder builder, Func<IServiceProvider, IDriver> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        GraphitiRegistration.Of(builder).Driver = services => new Neo4jDriver(factory(services));
        return builder;
    }
}
