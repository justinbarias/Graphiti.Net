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

    [Theory]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:str\", \"args\": [1.0]}", "{\"ok\": \"1.0\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:str\", \"args\": [\"a'b\"]}", "{\"ok\": \"a'b\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:repr\", \"args\": [\"a'b\"]}", "{\"ok\": \"\\\"a'b\\\"\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:repr\", \"args\": [1000]}", "{\"ok\": \"1000\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:list\", \"args\": [{\"$tuple\": [\"a\", \"b\"]}]}", "{\"ok\": [\"a\", \"b\"]}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:list\", \"args\": [5]}", "{\"error\": \"TypeError\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:list\", \"args\": [\"ab\"]}", "{\"host_error\": \"JudgeHostErrorException\"}")]
    [InlineData("{\"id\": \"x\", \"fn\": \"builtins:str\", \"args\": [{\"$ref\": \"no.such.module:NAME\"}]}", "{\"error\": \"JudgeNotBound\"}")]
    public async Task The_builtins_answer_as_Python_does(string line, string expected)
    {
        Assert.Equal(expected, await HostLoop.AnswerAsync(line));
    }

    [Fact]
    public void Load_calls_Register_of_each_unit_class()
    {
        var table = Bindings.Load([typeof(Graphiti.JudgeHost.Units.GoodProbeBindings)]);

        Assert.Contains("probe:good", table.Pure.Keys);
    }

    [Theory]
    [InlineData(typeof(Graphiti.JudgeHost.Unit.WrongNamespaceBindings), "is not a unit class")]
    [InlineData(typeof(Graphiti.JudgeHost.Units.WrongNameProbe), "is not a unit class")]
    [InlineData(typeof(Graphiti.JudgeHost.Units.NoRegisterBindings), "has no static method Register")]
    [InlineData(typeof(Graphiti.JudgeHost.Units.WrongSignatureBindings), "has no static method Register")]
    [InlineData(typeof(Graphiti.JudgeHost.Units.ThrowingBindings), "Register failed")]
    [InlineData(typeof(Graphiti.JudgeHost.Units.DuplicateBindings), "Register failed")]
    public void Load_stops_for_a_unit_class_that_would_bind_nothing_or_bind_wrong(Type unit, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Bindings.Load([unit]));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_assembly_has_no_unit_class_that_Load_rejects()
    {
        var table = Bindings.Load(typeof(Bindings).Assembly.GetTypes());

        Assert.Equal(Bindings.Pure.Keys.Order(StringComparer.Ordinal), table.Pure.Keys.Order(StringComparer.Ordinal));
    }
}
