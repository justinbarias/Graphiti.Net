// Ported from tests/ingestion/test_analyze_bulk.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/phases.py (analyze_bulk). Packet P9g of plan 0004, layer 9.
//
// The one test of the file replaces graphiti.extract_nodes_and_edges_bulk, node_operations._collect_candidate_nodes,
// phases.dedupe_edges_bulk, and graphiti.resolve_extracted_edges, and it binds two methods of `Graphiti` to the fake
// `Graphiti` of the Harness. No fake at the network boundary gives these results (open item O10):
// TODO(port): O10 test_bulk_analysis_does_not_create_a_node_that_is_in_the_graph. Covered in part: the graphiti case
//   analyze_bulk, which runs analyze_bulk on a real Graphiti.
namespace Graphiti.Port.Evaluation;
