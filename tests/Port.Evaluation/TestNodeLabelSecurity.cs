// Ported from tests/test_node_label_security.py (port/RULES.md, R25). Only the tests whose code under test is in
// graphiti_core/nodes.py (the label validation of Node), helpers.validate_node_labels, and
// models/nodes/node_db_queries.py.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Models.Nodes;
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

    [Fact]
    public void test_entity_node_save_query_rejects_unsafe_labels_when_validation_is_bypassed()
    {
        var error = Assert.Throws<NodeLabelValidationException>(
            () => NodeDbQueries.GetEntityNodeSaveQuery(
                GraphProvider.Neo4j,
                "Entity:Entity`) WITH n MATCH (x) DETACH DELETE x //"));
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_entity_node_save_bulk_query_rejects_unsafe_labels_when_validation_is_bypassed()
    {
        var error = Assert.Throws<NodeLabelValidationException>(
            () => NodeDbQueries.GetEntityNodeSaveBulkQuery(
                GraphProvider.Falkordb,
                [
                    new()
                    {
                        ["uuid"] = "node-1",
                        ["name"] = "Alice",
                        ["group_id"] = "group",
                        ["summary"] = "summary",
                        ["created_at"] = "2024-01-01T00:00:00Z",
                        ["name_embedding"] = new List<object?> { 0.1, 0.2 },
                        ["labels"] = new List<object?> { "Entity", "Entity`) WITH n MATCH (x) DETACH DELETE x //" },
                    },
                ]));
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }
}
