// Evaluation test for graphiti_core/cross_encoder/client.py (port/RULES.md, R25). Upstream has no test file for the
// module: the tests that name CrossEncoderClient only build a Mock(spec=CrossEncoderClient) for a Graphiti object.
// values/llm_client.json holds what port/tools/record_llm_client.py recorded from Python for this module.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.CrossEncoder;
using static Graphiti.Port.Evaluation.LlmClientClientSupport;

public class CrossEncoderClientTests
{
    // Python: CrossEncoderClient() raises TypeError, because `rank` is abstract. The C# class is abstract.
    [Fact]
    public void CrossEncoderClient_cannot_be_instantiated()
    {
        Assert.Contains("CrossEncoderClient", Items(Dict(Document["constants"])["abstract_classes_refuse_instances"]).Cast<string>());
        Assert.True(typeof(CrossEncoderClient).IsAbstract);
        Assert.True(typeof(CrossEncoderClient).GetMethod(nameof(CrossEncoderClient.RankAsync), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.IsAbstract);
    }
}
