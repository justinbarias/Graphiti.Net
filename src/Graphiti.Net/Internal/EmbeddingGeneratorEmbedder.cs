namespace Graphiti.Internal;

using Graphiti.Core.Embedder;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;

// DESIGN: the design names "IEmbeddingGenerator" with no type arguments, and says that an Embedding<float> value is
// widened and an Embedding<double> value is copied. The adapter has one constructor for each of the two generators.

/// <summary>
/// The embedding adapter of the public surface design: the ported <see cref="EmbedderClient"/> on a host
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (decision D28). It follows upstream's OpenAI embedder: one
/// generator call, and each vector cut to <see cref="EmbedderConfig.EmbeddingDim"/> on the client.
/// </summary>
internal sealed class EmbeddingGeneratorEmbedder : EmbedderClient
{
    private readonly Func<List<string>, EmbeddingGenerationOptions?, CancellationToken, Task<List<List<double>>>> generate;
    private readonly EmbedderConfig config;
    private readonly string? model;

    /// <summary>Initializes a new instance of the <see cref="EmbeddingGeneratorEmbedder"/> class for a generator of 32-bit vectors.</summary>
    /// <param name="generator">The host embedding generator.</param>
    /// <param name="config">The embedding size. Null is the default <see cref="EmbedderConfig"/>.</param>
    /// <param name="model">The option <c>Embedding.Model</c>. Null sends no model id.</param>
    internal EmbeddingGeneratorEmbedder(
        IEmbeddingGenerator<string, Embedding<float>> generator, EmbedderConfig? config = null, string? model = null)
        : this(Of(generator), config, model)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmbeddingGeneratorEmbedder"/> class for a generator of 64-bit vectors.</summary>
    /// <param name="generator">The host embedding generator.</param>
    /// <param name="config">The embedding size. Null is the default <see cref="EmbedderConfig"/>.</param>
    /// <param name="model">The option <c>Embedding.Model</c>. Null sends no model id.</param>
    internal EmbeddingGeneratorEmbedder(
        IEmbeddingGenerator<string, Embedding<double>> generator, EmbedderConfig? config = null, string? model = null)
        : this(Of(generator), config, model)
    {
    }

    private EmbeddingGeneratorEmbedder(
        Func<List<string>, EmbeddingGenerationOptions?, CancellationToken, Task<List<List<double>>>> generate,
        EmbedderConfig? config,
        string? model)
    {
        this.generate = generate;
        this.config = config ?? new EmbedderConfig();
        this.model = model;
    }

    /// <summary><c>create(input_data)</c>: one generator call with the caller's list, then the first vector.</summary>
    internal override async Task<List<double>> CreateAsync(
        List<string> inputData,
        CancellationToken cancellationToken = default)
    {
        var vectors = await GenerateAsync("embed", inputData, cancellationToken);
        return Cut(vectors[0]);
    }

    /// <summary><c>create_batch(input_data_list)</c>: one generator call, results in input order.</summary>
    internal override async Task<List<List<double>>> CreateBatchAsync(
        List<string> inputDataList,
        CancellationToken cancellationToken = default)
    {
        var vectors = await GenerateAsync("embed_batch", inputDataList, cancellationToken);
        return vectors.Select(Cut).ToList();
    }

    // An Embedding<float> value is widened to double.
    private static Func<List<string>, EmbeddingGenerationOptions?, CancellationToken, Task<List<List<double>>>> Of(
        IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return async (inputs, options, token) =>
        {
            var embeddings = await generator.GenerateAsync(inputs, options, token);
            return embeddings.Select(e => Widen(e.Vector.Span)).ToList();
        };
    }

    // An Embedding<double> value is copied as it is.
    private static Func<List<string>, EmbeddingGenerationOptions?, CancellationToken, Task<List<List<double>>>> Of(
        IEmbeddingGenerator<string, Embedding<double>> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return async (inputs, options, token) =>
        {
            var embeddings = await generator.GenerateAsync(inputs, options, token);
            return embeddings.Select(e => e.Vector.ToArray().ToList()).ToList();
        };
    }

    private static List<double> Widen(ReadOnlySpan<float> vector)
    {
        var result = new List<double>(vector.Length);
        foreach (var value in vector)
        {
            result.Add(value);
        }

        return result;
    }

    private Task<List<List<double>>> GenerateAsync(string operation, List<string> inputs, CancellationToken cancellationToken)
    {
        // The adapter sends no EmbeddingGenerationOptions.Dimensions: upstream cuts on the client and sends no size.
        var options = string.IsNullOrEmpty(model) ? null : new EmbeddingGenerationOptions { ModelId = model };

        // DESIGN: ModelCallInfo.Model is "the model argument of _generate_response", which an embedding call has not, so it is null.
        var info = new ModelCallInfo(operation, null, null, null, null, null);
        return GraphitiModelCall.CallHostAsync(info, token => generate(inputs, options, token), cancellationToken);
    }

    // `embedding[: self.config.embedding_dim]`
    private List<double> Cut(List<double> vector) => Py.Slice(vector, null, config.EmbeddingDim);
}
