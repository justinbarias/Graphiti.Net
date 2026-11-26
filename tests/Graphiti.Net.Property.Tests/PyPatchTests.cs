namespace Graphiti.PyCompat.Tests;

using Graphiti.PyCompat;

public class PyPatchTests
{
    private static readonly PyPatch<double> Threshold = new(0.15);
    private static readonly PyPatch<long> MinTokens = new(1000);
    private static readonly PyPatch<string?> Name = new(null);

    [Fact]
    public void The_value_of_the_module_is_read_as_the_value_itself()
    {
        double threshold = Threshold;

        Assert.Equal(0.15, threshold);
        Assert.Equal(0.15, Threshold.Value);
        Assert.True(MinTokens > 999);
        Assert.Equal(2000, MinTokens * 2);
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
    public void The_text_is_the_Python_text_of_the_value()
    {
        Assert.Equal("0.15", Threshold.ToString());
        Assert.Equal("1000", $"{MinTokens}");
        using (Threshold.Use(1.0))
        {
            Assert.Equal("1.0", Threshold.ToString());
        }
    }
}
