// Ported from tests/test_node_label_security.py (port/RULES.md, R25). Only the tests whose code under test is in
// graphiti_core/nodes.py (the label validation of Node) and helpers.validate_node_labels.
// Two tests wait for graphiti_core/models/nodes/node_db_queries.py, which is a skeleton today:
// test_entity_node_save_query_rejects_unsafe_labels_when_validation_is_bypassed and
// test_entity_node_save_bulk_query_rejects_unsafe_labels_when_validation_is_bypassed.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.PyCompat;

public class TestNodeLabelSecurity
{
    [Fact]
    public void test_entity_node_rejects_unsafe_labels()
    {
        var error = Assert.Throws<ValidationException>(
            () => new EntityNode
            {
                Name = "Alice",
                GroupId = "group",
                Labels = ["Entity`) WITH n MATCH (x) DETACH DELETE x //"],
            }.Validate());
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_entity_node_assignment_rejects_unsafe_labels()
    {
        var node = new EntityNode { Name = "Alice", GroupId = "group", Labels = ["Person"] }.Validate();

        var error = Assert.Throws<ValidationException>(
            () => node.Labels = ["Entity`) WITH n MATCH (x) DETACH DELETE x //"]);
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }
}
