// Evaluation test for graphiti_core/embedder/client.py (port/RULES.md, R23 and R25). Upstream has no test file for the
// module: tests/embedder/test_openai.py and the other files test the concrete embedders, which wait for their units.
// values/llm_client.json holds what port/tools/record_llm_client.py recorded from Python for this module.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Embedder;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.LlmClientClientSupport;

public class EmbedderClientTests
{
    [Fact]
    public void EMBEDDING_DIM_and_the_default_of_EmbedderConfig_match_Python()
    {
        var constants = Dict(Document["constants"]);
        Assert.Equal(Py.AsLong(constants["EMBEDDING_DIM"]), Client.EmbeddingDim);
        Assert.Equal(Py.AsLong(constants["embedding_dim_of_default_config"]), new EmbedderConfig().EmbeddingDim);
    }

    // Python: Field(default=EMBEDDING_DIM, frozen=True). The init accessor is the C# form of a frozen field (R7).
    [Fact]
    public void The_field_embedding_dim_of_EmbedderConfig_is_frozen()
    {
        Assert.True((bool)Dict(Document["constants"])["embedder_config_frozen_field"]!);
        var setter = typeof(EmbedderConfig).GetProperty(nameof(EmbedderConfig.EmbeddingDim), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.SetMethod!;
        Assert.Contains(setter.ReturnParameter.GetRequiredCustomModifiers(), t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        Assert.Equal(7, new EmbedderConfig { EmbeddingDim = 7 }.EmbeddingDim);
    }

    [Fact]
    public async Task create_batch_raises_NotImplementedError_with_no_text_unless_a_subclass_overrides_it()
    {
        var error = Dict(Dict(Document["constants"])["create_batch_error"]);
        Assert.Equal("NotImplementedError", (string)error["type"]!);

        var thrown = await Assert.ThrowsAsync<NotImplementedException>(() => new OnlyCreate().CreateBatchAsync(["a"], TestContext.Current.CancellationToken));
        Assert.Equal((string)error["str"]!, thrown.Message);
    }

    // Python: EmbedderClient() raises TypeError, because `create` is abstract. The C# class is abstract.
    [Fact]
    public void EmbedderClient_cannot_be_instantiated()
    {
        Assert.Contains("EmbedderClient", Items(Dict(Document["constants"])["abstract_classes_refuse_instances"]).Cast<string>());
        Assert.True(typeof(EmbedderClient).IsAbstract);
        Assert.True(typeof(EmbedderClient).GetMethod(nameof(EmbedderClient.CreateAsync), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.IsAbstract);
    }

    private sealed class OnlyCreate : EmbedderClient
    {
        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
            Task.FromResult<List<double>>([0.5]);
    }
}
