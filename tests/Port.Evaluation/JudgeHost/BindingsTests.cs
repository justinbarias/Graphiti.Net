namespace Port.Evaluation.JudgeHost;

using Graphiti.JudgeHost;
using Graphiti.JudgeHost.Core;

/// <summary>The table that <see cref="Bindings"/> builds from the unit files.</summary>
public sealed class BindingsTests
{
    [Fact]
    public void The_table_holds_the_two_judge_functions()
    {
        Assert.Contains("judge:embed", Bindings.Pure.Keys);
        Assert.Contains("judge:schema_text", Bindings.Pure.Keys);
    }

    [Fact]
    public async Task The_schema_of_a_model_that_no_unit_added_is_not_bound()
    {
        var line = await HostLoop.AnswerAsync("{\"id\": \"x\", \"fn\": \"judge:schema_text\", \"args\": [\"no.such.module:Model\"]}");

        Assert.Equal("{\"error\": \"JudgeNotBound\"}", line);
    }

    [Fact]
    public void Each_unit_class_is_loaded()
    {
        // A class in the unit namespace that the loader skips binds nothing, and its cases stay JudgeNotBound in silence.
        // A nested type is a closure class that the compiler made.
        var units = typeof(Bindings).Assembly.GetTypes().Where(type => type.Namespace == "Graphiti.JudgeHost.Units" && !type.IsNested).ToList();

        Assert.All(units, unit => Assert.EndsWith("Bindings", unit.Name, StringComparison.Ordinal));
    }
}
