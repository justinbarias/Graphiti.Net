// Tests of the embedding adapter EmbeddingGeneratorEmbedder by the table of "The embedding adapter" in
// docs/design-docs/public-surface-design.md. The generator is a scripted fake at the network boundary.
namespace Graphiti.Port.Evaluation;

using Graphiti;
using Graphiti.Core.Embedder;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class ModelAdapterEmbeddingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static float[] VectorOf(string text) => [text.Length, 2, 3, 4];

    [Fact]
    public async Task Create_sends_the_callers_list_and_returns_the_first_vector()
    {
        var generator = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var embedder = new EmbeddingGeneratorEmbedder(generator, new EmbedderConfig { EmbeddingDim = 4 });

        var vector = await Asyncio.Run(() => embedder.CreateAsync(["abc", "z"], Token));

        Assert.Single(generator.Calls);
        Assert.Equal(["abc", "z"], generator.Calls[0].Inputs);
        Assert.Equal([3.0, 2, 3, 4], vector);
    }

    [Fact]
    public async Task Create_batch_makes_one_call_and_returns_the_vectors_in_input_order()
    {
        var generator = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var embedder = new EmbeddingGeneratorEmbedder(generator, new EmbedderConfig { EmbeddingDim = 4 });

        var vectors = await Asyncio.Run(() => embedder.CreateBatchAsync(["a", "bbb", "cc"], Token));

        Assert.Single(generator.Calls);
        Assert.Equal(["a", "bbb", "cc"], generator.Calls[0].Inputs);
        Assert.Equal([1.0, 3.0, 2.0], vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task Each_vector_is_cut_to_the_embedding_dimension_and_no_size_is_sent()
    {
        var generator = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var embedder = new EmbeddingGeneratorEmbedder(generator, new EmbedderConfig { EmbeddingDim = 2 }, "m");

        var one = await Asyncio.Run(() => embedder.CreateAsync(["abc"], Token));
        var batch = await Asyncio.Run(() => embedder.CreateBatchAsync(["a", "bb"], Token));

        Assert.Equal([3.0, 2], one);
        Assert.Equal([[1.0, 2], [2.0, 2]], batch);
        Assert.All(generator.Calls, c => Assert.Null(c.Options?.Dimensions));
    }

    [Fact]
    public async Task A_vector_shorter_than_the_dimension_is_kept_whole()
    {
        var generator = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var embedder = new EmbeddingGeneratorEmbedder(generator, new EmbedderConfig { EmbeddingDim = 1024 });

        var vector = await Asyncio.Run(() => embedder.CreateAsync(["x"], Token));

        Assert.Equal(4, vector.Count);
    }

    [Fact]
    public async Task The_model_id_is_sent_only_if_the_option_has_a_value()
    {
        var with = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var without = new ScriptedEmbeddingGenerator<float>(VectorOf);

        await Asyncio.Run(() => new EmbeddingGeneratorEmbedder(with, model: "text-embedding-3-small").CreateAsync(["x"], Token));
        await Asyncio.Run(() => new EmbeddingGeneratorEmbedder(without).CreateBatchAsync(["x"], Token));

        Assert.Equal("text-embedding-3-small", with.Calls[0].Options?.ModelId);
        Assert.Null(without.Calls[0].Options?.ModelId);
    }

    [Fact]
    public async Task A_float_vector_is_widened_and_a_double_vector_is_copied_as_it_is()
    {
        var floats = new ScriptedEmbeddingGenerator<float>(_ => [0.1f, 1.0f / 3]);
        var doubles = new ScriptedEmbeddingGenerator<double>(_ => [0.1, 1.0 / 3]);

        var widened = await Asyncio.Run(() => new EmbeddingGeneratorEmbedder(floats).CreateAsync(["x"], Token));
        var copied = await Asyncio.Run(() => new EmbeddingGeneratorEmbedder(doubles).CreateAsync(["x"], Token));

        Assert.Equal([(double)0.1f, (double)(1.0f / 3)], widened);
        Assert.Equal([0.1, 1.0 / 3], copied);
    }

    [Fact]
    public async Task Inside_each_call_Current_names_the_operation_and_outside_it_is_null()
    {
        var generator = new ScriptedEmbeddingGenerator<float>(VectorOf);
        var embedder = new EmbeddingGeneratorEmbedder(generator);

        var after = await Asyncio.Run(async () =>
        {
            await embedder.CreateAsync(["x"], Token);
            await embedder.CreateBatchAsync(["x", "y"], Token);
            return GraphitiModelCall.Current;
        });

        Assert.Null(after);
        Assert.Null(GraphitiModelCall.Current);
        Assert.Equal(new ModelCallInfo("embed", null, null, null, null, null), generator.Calls[0].Info);
        Assert.Equal(new ModelCallInfo("embed_batch", null, null, null, null, null), generator.Calls[1].Info);
        Assert.All(generator.Calls, c => Assert.False(c.OnLoop));
    }
}
