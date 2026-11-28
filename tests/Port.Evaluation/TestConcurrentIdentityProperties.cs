// Ported from tests/ingestion/test_concurrent_identity_properties.py (port/RULES.md, R25). The code under test is
// graphiti_core/graphiti.py, graphiti_core/ingestion/scheduler.py, and graphiti_core/ingestion/phases.py. Packet P9g of
// plan 0004, layer 9.
//
// The first three tests replace scheduler.admit_episode, scheduler.commit_unit, scheduler.enrich_unit,
// phases.extract_nodes, phases.extract_attributes_only_from_nodes, node_operations._collect_exact_name_candidate_nodes,
// node_operations._semantic_candidate_search, and EntityNode.get_by_attribute_values, and they assert the arguments of
// these replacements. No fake at the network boundary gives these results (open item O10):
// TODO(port): O10 test_concurrent_add_episode_uses_identity_properties_for_lookup. Covered: the graphiti case
//   analyze_episode_identity.
// TODO(port): O10 test_concurrent_add_episode_resolves_same_identity_without_dedupe_model_call. Covered: the graphiti
//   case analyze_episode_identity.
// TODO(port): O10 test_concurrent_add_episode_without_identity_properties_skips_identity_calls. Covered: the graphiti
//   case analyze_episode.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestConcurrentIdentityProperties
{
    // `class PersonProperties(BaseModel): email: str`.
    private static OrderedDictionary<string, GraphTypeDescriptor> PersonTypes() => new()
    {
        ["Person"] = new GraphTypeDescriptor("PersonProperties", null, [new GraphFieldDescriptor("email", GraphFieldKind.String, false, null, true, null)]),
    };

    // PORT: `make_graphiti` builds a `Graphiti` with `__new__` and sets its members to the Harness. The GraphitiCore of the
    // Harness is that object: it has the clients, the tracer, and the driver of the Harness. The test gives it the
    // configuration of the Harness, and the configuration enables each group, as `_concurrent_ingestion_enabled`
    // returns True.
    private static GraphitiCore MakeGraphiti(Harness harness)
    {
        harness.Graphiti.ConcurrentIngestion = harness.Config;
        return harness.Graphiti;
    }

    [Theory(Timeout = 10000)]
    [InlineData("add_episode")]
    [InlineData("add_episode_bulk")]
    public async Task test_invalid_identity_properties_fail_before_concurrent_writes(string method)
    {
        await LoopGuard.RunAsync(async () =>
        {
            var harness = new Harness();
            var graphiti = MakeGraphiti(harness);

            // PORT: the test replaces scheduler.admit_episode and scheduler.admit_bulk with a function that calls
            // `begin_admit` and a graph write, and it asserts that neither runs. The port runs the real admission on the
            // store and the driver of the Harness, and it asserts that the store gets no `begin_admit` and the graph no
            // write (test_p9g_variants.py).
            await Assert.ThrowsAsync<IdentityPropertyValidationException>(async () =>
            {
                if (method == "add_episode")
                {
                    await graphiti.AddEpisodeAsync(
                        name: "invalid episode",
                        episodeBody: "content",
                        sourceDescription: "test",
                        referenceTime: DatetimeUtils.UtcNow(),
                        groupId: Group,
                        entityTypes: PersonTypes(),
                        identityProperties: new() { ["Missing"] = ["email"] },
                        cancellationToken: TestContext.Current.CancellationToken);
                }
                else
                {
                    await graphiti.AddEpisodeBulkAsync(
                        bulkEpisodes: [],
                        groupId: Group,
                        entityTypes: PersonTypes(),
                        identityProperties: new() { ["Missing"] = ["email"] },
                        cancellationToken: TestContext.Current.CancellationToken);
                }
            });

            Assert.Equal(0, harness.Store.BeginAdmitCount);
            Assert.Empty(harness.Graph.Log);
        });
    }
}
