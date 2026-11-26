namespace Graphiti.PyCompat.Tests;

using Graphiti.PyCompat;

public class PyPatchTests
{
    private static readonly PyPatch<double> Threshold = new(0.15);
    private static readonly PyPatch<long> MinTokens = new(1000);
    private static readonly PyPatch<string?> Name = new(null);

    [Fact]
    public void Value_reads_the_value_of_the_module()
    {
        Assert.Equal(0.15, Threshold.Value);
        Assert.True(MinTokens.Value > 999);
        Assert.Equal(2000, MinTokens.Value * 2);
        Assert.Null(Name.Value);
    }

    [Fact]
    public void Use_replaces_the_value_until_the_scope_ends_and_scopes_nest()
    {
        using (Threshold.Use(0.01))
        {
            Assert.Equal(0.01, Threshold.Value);
            using (Threshold.Use(0.5))
            {
                Assert.Equal(0.5, Threshold.Value);
            }

            Assert.Equal(0.01, Threshold.Value);
        }

        Assert.Equal(0.15, Threshold.Value);
    }

    [Fact]
    public void A_replacement_can_be_the_default_value_of_the_type()
    {
        var name = new PyPatch<string?>("module");
        using (name.Use(null))
        {
            Assert.Null(name.Value);
        }

        using (MinTokens.Use(0))
        {
            Assert.Equal(0, MinTokens.Value);
        }

        Assert.Equal("module", name.Value);
    }

    [Fact]
    public async Task A_replacement_reaches_the_tasks_of_its_flow_and_no_other_flow()
    {
        var patch = new PyPatch<long>(1);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var other = Task.Run(async () =>
        {
            await inside.Task;
            var seen = patch.Value;
            release.SetResult();
            return seen;
        });

        using (patch.Use(2))
        {
            inside.SetResult();
            await release.Task;
            Assert.Equal(2, await Task.Run(() => patch.Value));
            Assert.Equal(2, await Asyncio.Run(() => Task.FromResult(patch.Value)));
        }

        Assert.Equal(1, await other);
        Assert.Equal(1, patch.Value);
    }

    [Fact]
    public void The_text_is_the_Python_text_of_the_value_and_a_format_reaches_the_value()
    {
        Assert.Equal("0.15", Threshold.ToString());
        Assert.Equal("1000", $"{MinTokens}");
        Assert.Equal("0.150", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Threshold:F3}"));
        using (Threshold.Use(1.0))
        {
            Assert.Equal("1.0", Threshold.ToString());
            Assert.Equal("1.00", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Threshold:F2}"));
        }
    }

    [Fact]
    public void A_scope_that_is_not_the_innermost_one_cannot_end()
    {
        var patch = new PyPatch<long>(1000);
        var outer = patch.Use(1);
        var inner = patch.Use(2);

        Assert.Throws<InvalidOperationException>(outer.Dispose);
        Assert.Equal(2, patch.Value);

        inner.Dispose();
        Assert.Equal(1, patch.Value);
        outer.Dispose();
        Assert.Equal(1000, patch.Value);
    }

    [Fact]
    public void A_second_dispose_of_a_scope_changes_nothing()
    {
        var patch = new PyPatch<long>(1000);
        var first = patch.Use(1);
        first.Dispose();
        using (patch.Use(2))
        {
            first.Dispose();
            Assert.Equal(2, patch.Value);
        }

        Assert.Equal(1000, patch.Value);
    }
}
