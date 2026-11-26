namespace Graphiti.PyCompat.Tests;

using Graphiti.PyCompat;

/// <summary>Small members that the pilot units showed a need for (plan 0003, task 6).</summary>
public class SupportTests
{
    [Fact]
    public void Dumps_takes_an_indent_that_an_int_cannot_hold()
    {
        // json.dumps(5, indent=-2**40) is '5', and json.dumps([1, [2]], indent=-2**40) has line breaks and no spaces.
        Assert.Equal("5", PyJson.Dumps(5L, indent: -(1L << 40)));
        Assert.Equal("[\n1,\n[\n2\n]\n]", PyJson.Dumps(new List<object?> { 1L, new List<object?> { 2L } }, indent: -(1L << 40)));
        Assert.Equal("[\n  1\n]", PyJson.Dumps(new List<object?> { 1L }, indent: 2L));
    }

    [Fact]
    public void SubclassOf_gives_the_object_or_the_error_of_pydantic()
    {
        var model = new Marker();

        Assert.Same(model, Pydantic.SubclassOf<Marker>(model));
        // PromptSpec(name='n', response_model=1): Input should be a subclass of BaseModel [type=is_subclass_of]
        var error = Assert.Throws<ValidationException>(() => Pydantic.SubclassOf<Marker>(1L));
        var only = Assert.Single(error.Errors);
        Assert.Equal("is_subclass_of", only.Type);
        Assert.Equal("Input should be a subclass of BaseModel", only.Msg);
        Assert.Equal("is_subclass_of", Assert.Throws<ValidationException>(() => Pydantic.SubclassOf<Marker>(null)).Errors[0].Type);
        Assert.Equal("Input should be a subclass of Node", Assert.Throws<ValidationException>(() => Pydantic.SubclassOf<Marker>("x", "Node")).Errors[0].Msg);
    }

    private sealed class Marker;
}
