// Evaluation test for graphiti_core/nodes.py (port/RULES.md, R8).
// This test has no Python original: Python has no cancellation token. Rule R8 says that each async method takes the token
// and passes it on to each awaited call, so a canceled token must reach the fake driver, session, and embedder, which throw
// OperationCanceledException with ThrowIfCancellationRequested.
namespace Graphiti.Port.Evaluation;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.PyCompat;

public partial class NodesValuesTests
{
    private static readonly OrderedDictionary<string, Func<GraphDriver, EmbedderClient, CancellationToken, Task>> CancelableCalls = new()
    {
        ["EntityNode.delete"] = (driver, _, token) => ((EntityNode)ModelValidate("EntityNode", BaseKwargs("EntityNode"))).DeleteAsync(driver, token),
        ["CommunityNode.delete"] = (driver, _, token) => ((CommunityNode)ModelValidate("CommunityNode", BaseKwargs("CommunityNode"))).DeleteAsync(driver, token),
        ["Node.delete_by_group_id"] = (driver, _, token) => Node.DeleteByGroupIdAsync(driver, "g", cancellationToken: token),
        ["Node.delete_by_uuids"] = (driver, _, token) => Node.DeleteByUuidsAsync(driver, ["a"], cancellationToken: token),
        ["EpisodicNode.save"] = (driver, _, token) => ((EpisodicNode)ModelValidate("EpisodicNode", BaseKwargs("EpisodicNode"))).SaveAsync(driver, token),
        ["EpisodicNode.delete"] = (driver, _, token) => ((EpisodicNode)ModelValidate("EpisodicNode", BaseKwargs("EpisodicNode"))).DeleteAsync(driver, token),
        ["EpisodicNode.get_by_uuid"] = (driver, _, token) => EpisodicNode.GetByUuidAsync(driver, "u", cancellationToken: token),
        ["EpisodicNode.get_by_uuids"] = (driver, _, token) => EpisodicNode.GetByUuidsAsync(driver, ["u"], cancellationToken: token),
        ["EpisodicNode.get_by_group_ids"] = (driver, _, token) => EpisodicNode.GetByGroupIdsAsync(driver, ["g"], cancellationToken: token),
        ["EpisodicNode.get_by_entity_node_uuid"] = (driver, _, token) => EpisodicNode.GetByEntityNodeUuidAsync(driver, "n", token),
        ["EntityNode.generate_name_embedding"] = (_, embedder, token) => ((EntityNode)ModelValidate("EntityNode", BaseKwargs("EntityNode"))).GenerateNameEmbeddingAsync(embedder, token),
        ["EntityNode.load_name_embedding"] = (driver, _, token) => ((EntityNode)ModelValidate("EntityNode", BaseKwargs("EntityNode"))).LoadNameEmbeddingAsync(driver, token),
        ["EntityNode.save"] = (driver, _, token) => ((EntityNode)ModelValidate("EntityNode", BaseKwargs("EntityNode"))).SaveAsync(driver, token),
        ["EntityNode.get_by_uuid"] = (driver, _, token) => EntityNode.GetByUuidAsync(driver, "u", token),
        ["EntityNode.get_by_uuids"] = (driver, _, token) => EntityNode.GetByUuidsAsync(driver, ["u"], cancellationToken: token),
        ["EntityNode.get_by_names"] = (driver, _, token) => EntityNode.GetByNamesAsync(driver, ["a"], ["g"], token),
        ["EntityNode.get_by_attribute_values"] = (driver, _, token) => EntityNode.GetByAttributeValuesAsync(driver, "g", new() { ["email"] = "a@b" }, cancellationToken: token),
        ["EntityNode.get_by_group_ids"] = (driver, _, token) => EntityNode.GetByGroupIdsAsync(driver, ["g"], cancellationToken: token),
        ["CommunityNode.save"] = (driver, _, token) => ((CommunityNode)ModelValidate("CommunityNode", BaseKwargs("CommunityNode"))).SaveAsync(driver, token),
        ["CommunityNode.generate_name_embedding"] = (_, embedder, token) => ((CommunityNode)ModelValidate("CommunityNode", BaseKwargs("CommunityNode"))).GenerateNameEmbeddingAsync(embedder, token),
        ["CommunityNode.load_name_embedding"] = (driver, _, token) => ((CommunityNode)ModelValidate("CommunityNode", BaseKwargs("CommunityNode"))).LoadNameEmbeddingAsync(driver, token),
        ["CommunityNode.get_by_uuid"] = (driver, _, token) => CommunityNode.GetByUuidAsync(driver, "u", token),
        ["CommunityNode.get_by_uuids"] = (driver, _, token) => CommunityNode.GetByUuidsAsync(driver, ["u"], token),
        ["CommunityNode.get_by_group_ids"] = (driver, _, token) => CommunityNode.GetByGroupIdsAsync(driver, ["g"], cancellationToken: token),
        ["SagaNode.save"] = (driver, _, token) => ((SagaNode)ModelValidate("SagaNode", BaseKwargs("SagaNode"))).SaveAsync(driver, token),
        ["SagaNode.delete"] = (driver, _, token) => ((SagaNode)ModelValidate("SagaNode", BaseKwargs("SagaNode"))).DeleteAsync(driver, token),
        ["SagaNode.get_by_uuid"] = (driver, _, token) => SagaNode.GetByUuidAsync(driver, "u", token),
        ["SagaNode.get_by_uuids"] = (driver, _, token) => SagaNode.GetByUuidsAsync(driver, ["u"], token),
        ["SagaNode.get_by_group_ids"] = (driver, _, token) => SagaNode.GetByGroupIdsAsync(driver, ["g"], cancellationToken: token),
        ["module.create_entity_node_embeddings"] = (_, embedder, token) => Nodes.CreateEntityNodeEmbeddingsAsync(
            embedder,
            [(EntityNode)ModelValidate("EntityNode", BaseKwargs("EntityNode"))],
            token),
    };

    public static TheoryData<string> CancelableNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CancelableCalls.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CancelableNames))]
    public async Task Each_async_method_throws_OperationCanceledException_for_a_canceled_token(string name)
    {
        var calls = new List<JsonObject>();
        var driver = new FakeDriver(calls, GraphProvider.Neo4j, []);
        var embedder = new FakeEmbedder(calls, "echo");

        await Assert.ThrowsAsync<OperationCanceledException>(() => CancelableCalls[name](driver, embedder, new CancellationToken(true)));
    }

    [Fact]
    public void The_cancellation_test_names_each_async_method_of_the_four_node_classes()
    {
        var methods = Root.GetProperty("methods").EnumerateArray().Select(m => m.GetProperty("input")).Select(i => i.GetProperty("class").GetString() + "." + i.GetProperty("method").GetString());
        Assert.Equal(
            [.. methods.Distinct().Order(StringComparer.Ordinal)],
            [.. CancelableCalls.Keys.Order(StringComparer.Ordinal)]);
    }
}
