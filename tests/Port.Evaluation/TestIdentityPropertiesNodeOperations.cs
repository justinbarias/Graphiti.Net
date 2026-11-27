// Ported from tests/utils/maintenance/test_identity_properties.py (port/RULES.md, R25). Only the tests whose code under
// test is in graphiti_core/utils/maintenance/node_operations.py. NodesTestIdentityProperties has the tests of
// EntityNode.get_by_attribute_values. The tests of validate_identity_properties wait for
// utils/ontology_utils/entity_types_utils.py, and the tests of Graphiti.add_episode, add_episode_bulk, and add_triplet
// wait for graphiti.py (layer 9).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public partial class TestIdentityProperties
{
    // class PersonWithRequiredFields(BaseModel): email: str, full_name: str
    private static readonly GraphTypeDescriptor PersonWithRequiredFields = new(
        "PersonWithRequiredFields",
        null,
        [
            new GraphFieldDescriptor("email", GraphFieldKind.String, false, null, true, null),
            new GraphFieldDescriptor("full_name", GraphFieldKind.String, false, null, true, null),
        ]);

    [Theory(Timeout = 5000)]
    [InlineData("neptune")]
    public async Task test_identity_candidate_collection_ignores_unsupported_provider(string provider)
    {
        var driver = new MockGraphDriver(PyEnum.FromValue<GraphProvider>(provider));
        var clients = MakeClients(driver: driver, maxCoroutines: 1);
        var node = Node("Alex", labels: ["Entity", "Person"], attributes: new() { ["email"] = "alex@example.com" });

        var result = await LoopGuard.RunAsync(() => NodeOperations.CollectIdentityPropertyCandidateNodesAsync(
            clients,
            [node],
            new OrderedDictionary<string, List<string>> { ["Person"] = ["email"] },
            TestContext.Current.CancellationToken));

        Assert.Empty(Assert.Single(result));
        Assert.Equal(0, driver.ExecuteQueryCalls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_identity_candidate_collection_calls_entity_attribute_lookup()
    {
        // PORT: monkeypatch.setattr(EntityNode, 'get_by_attribute_values', lookup) has no C# form (O10).
        // EntityNode.get_by_attribute_values hands its arguments to driver.graph_operations_interface after it checks the
        // names, so the lookup of the test is on that interface, and the real method runs.
        var candidate = Node("Alex", labels: ["Entity", "Person"]);
        var ops = new MockOps { GetByAttributeValues = (_, _, _, _) => [candidate] };
        var driver = new MockGraphDriver(ops: ops);
        var clients = MakeClients(driver: driver, maxCoroutines: 1);
        var node = Node("Alex", labels: ["Entity", "Person"], attributes: new() { ["email"] = "alex@example.com" });

        var result = await LoopGuard.RunAsync(() => NodeOperations.CollectIdentityPropertyCandidateNodesAsync(
            clients,
            [node],
            new OrderedDictionary<string, List<string>> { ["Person"] = ["email"] },
            TestContext.Current.CancellationToken));

        Assert.Same(candidate, Assert.Single(Assert.Single(result)));
        var call = Assert.Single(ops.AttributeCalls);
        Assert.Same(driver, call.Driver);
        Assert.Equal("group", call.GroupId);
        Assert.Equal(PyJson.Dumps(new OrderedDictionary<string, object?> { ["email"] = "alex@example.com" }), PyJson.Dumps(call.Attributes));
        Assert.Equal(["Person"], call.Labels);
        Assert.Equal(15, call.Limit);
    }

    [Fact(Timeout = 5000)]
    public async Task test_pre_resolution_attribute_extraction_skips_shape_validation()
    {
        // PORT: monkeypatch.setattr on `node_operations.generate_prompt_response` has no C# form (O10). The legacy path of
        // generate_prompt_response calls llm_client.generate_response first, so the reply of the test is the reply of the
        // LLM client, and the real function runs.
        var llm = new MockLlm { ReturnValue = new OrderedDictionary<string, object?> { ["email"] = "alex@example.com" } };
        var clients = MakeClients(llm, maxCoroutines: 1);
        var node = Node("Alex", labels: ["Entity", "Person"]);

        await LoopGuard.RunAsync(() => NodeOperations.ExtractAttributesOnlyFromNodesAsync(
            clients,
            [node],
            entityTypes: new OrderedDictionary<string, GraphTypeDescriptor> { ["Person"] = PersonWithRequiredFields },
            validateShape: false,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(PyJson.Dumps(new OrderedDictionary<string, object?> { ["email"] = "alex@example.com" }), PyJson.Dumps(node.Attributes));
        Assert.Single(llm.Calls);
    }
}
