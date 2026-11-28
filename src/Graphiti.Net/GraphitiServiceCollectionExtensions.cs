namespace Microsoft.Extensions.DependencyInjection;

using Graphiti;
using Graphiti.Internal;
using Microsoft.Extensions.AI;

/// <summary>The registration of Graphiti with dependency injection.</summary>
public static class GraphitiServiceCollectionExtensions
{
    /// <summary>
    /// Registers Graphiti: <c>IGraphiti</c> as a singleton, and its options. Add a driver with <c>AddNeo4j</c> on the
    /// returned builder. Graphiti resolves the unkeyed <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator"/> of
    /// the container. The builder can replace each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first resolution of <c>IGraphiti</c> validates the options, sets the <c>ILoggerFactory</c> and the
    /// <see cref="TimeProvider"/> of the container for the process-wide part of Graphiti, and fails with a message that
    /// names the missing call if the driver, the chat client, or the embedding generator is missing. With no
    /// registered <see cref="TimeProvider"/>, Graphiti uses <see cref="TimeProvider.System"/>. The library builds no
    /// index at start: call <c>BuildIndicesAndConstraintsAsync</c>.
    /// </para>
    /// <para>
    /// One container holds one Graphiti. The scheduler of Graphiti and the logger factory, the clock, and the failure
    /// classifier that ported code reads are process-wide, so a second container in the process shares them. Each
    /// container has its own <c>IGraphiti</c>, and the latest first resolution of an <c>IGraphiti</c> sets the logger
    /// factory and the clock for the process. A second call of this method on one service collection adds its
    /// <paramref name="configure"/> action and registers nothing twice.
    /// </para>
    /// <para>
    /// Graphiti closes the driver when the container disposes it, with the sync <c>Dispose</c> or the async dispose.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action that sets the options, or null.</param>
    /// <returns>The builder of Graphiti.</returns>
    public static IGraphitiBuilder AddGraphiti(this IServiceCollection services, Action<GraphitiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return GraphitiRegistration.Add(services, configure);
    }
}

/// <summary>The builder that <see cref="GraphitiServiceCollectionExtensions.AddGraphiti"/> returns.</summary>
public interface IGraphitiBuilder
{
    /// <summary>Gets the service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Gives Graphiti its own chat client in place of the unkeyed <see cref="IChatClient"/> of the container.</summary>
    /// <param name="factory">A function that returns the chat client.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory);

    /// <summary>
    /// Gives Graphiti its own embedding generator of 32-bit vectors in place of the embedding generator of the
    /// container.
    /// </summary>
    /// <param name="factory">A function that returns the embedding generator.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseEmbeddingGenerator(Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> factory);

    /// <summary>
    /// Gives Graphiti its own embedding generator of 64-bit vectors in place of the embedding generator of the
    /// container.
    /// </summary>
    /// <param name="factory">A function that returns the embedding generator.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseEmbeddingGenerator(Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<double>>> factory);

    /// <summary>
    /// Gives Graphiti a cross-encoder in place of the default one, which ranks passages with the chat client and needs
    /// <see cref="UseChatLogProbabilities"/>.
    /// </summary>
    /// <param name="factory">A function that returns the cross-encoder.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseCrossEncoder(Func<IServiceProvider, ICrossEncoder> factory);

    /// <summary>Gives Graphiti the keyed <see cref="IChatClient"/> of the container with this service key.</summary>
    /// <param name="serviceKey">The service key of the chat client.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseKeyedChatClient(object serviceKey);

    /// <summary>
    /// Gives the default cross-encoder the hook that asks for token log probabilities and reads them (decision D30).
    /// Without it, the first rerank call throws <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="factory">A function that returns the hook.</param>
    /// <returns>The builder.</returns>
    IGraphitiBuilder UseChatLogProbabilities(Func<IServiceProvider, IChatLogProbabilities> factory);
}
