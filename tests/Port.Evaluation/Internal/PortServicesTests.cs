namespace Graphiti.Port.Evaluation.Internal;

using System.Net;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class PortServicesTests
{
    [Theory]
    [InlineData(429, ModelCallFailure.RateLimit)]
    [InlineData(500, ModelCallFailure.ServerError)]
    [InlineData(503, ModelCallFailure.ServerError)]
    [InlineData(599, ModelCallFailure.ServerError)]
    [InlineData(600, ModelCallFailure.None)]
    [InlineData(499, ModelCallFailure.None)]
    [InlineData(400, ModelCallFailure.None)]
    [InlineData(401, ModelCallFailure.None)]
    [InlineData(200, ModelCallFailure.None)]
    public void The_default_classifier_reads_the_status_of_an_http_error(int status, ModelCallFailure expected)
    {
        var error = new HttpRequestException("failed", null, (HttpStatusCode)status);

        Assert.Equal(expected, PortServices.DefaultClassifyModelFailure(error));
    }

    [Fact]
    public void The_default_classifier_gives_None_for_an_http_error_with_no_status_and_for_other_errors()
    {
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new HttpRequestException("no status")));
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new TimeoutException()));
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new InvalidOperationException("429")));
    }

    [Fact]
    public void ClassifyModelFailure_calls_the_classifier_of_the_scope()
    {
        var error = new TimeoutException();
        Assert.Equal(ModelCallFailure.None, PortServices.ClassifyModelFailure(error));

        using (PortServices.Use(modelFailureClassifier: e => ReferenceEquals(e, error) ? ModelCallFailure.RateLimit : ModelCallFailure.None))
        {
            Assert.Equal(ModelCallFailure.RateLimit, PortServices.ClassifyModelFailure(error));
        }

        Assert.Equal(ModelCallFailure.None, PortServices.ClassifyModelFailure(error));
    }

    [Fact]
    public void Logger_uses_the_factory_of_the_scope_and_the_full_name_of_the_module_class()
    {
        using var factory = new RecordingFactory();
        using (PortServices.Use(loggerFactory: factory))
        {
            _ = PortServices.Logger(typeof(PortServicesTests));
        }

        Assert.Equal(["Graphiti.Port.Evaluation.Internal.PortServicesTests"], factory.Categories);
    }

    [Fact]
    public void Use_keeps_the_values_of_the_scope_around_it_and_restores_them()
    {
        var outer = new FixedTime();
        var random = new PyRandom();
        using (PortServices.Use(time: outer))
        {
            using (PortServices.Use(random: random))
            {
                Assert.Same(outer, PortServices.Time);
                Assert.Same(random, PortServices.Random);
            }

            Assert.Same(outer, PortServices.Time);
            Assert.NotSame(random, PortServices.Random);
        }

        Assert.NotSame(outer, PortServices.Time);
    }

    [Fact]
    public async Task A_scope_is_not_visible_to_a_flow_that_started_outside_it()
    {
        // Tests run at the same time. A value of one test must not reach another test.
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scoped = new FixedTime();
        var other = Task.Run(async () =>
        {
            await inside.Task;
            var seen = PortServices.Time;
            release.SetResult();
            return seen;
        });

        using (PortServices.Use(time: scoped))
        {
            inside.SetResult();
            await release.Task;
            Assert.Same(scoped, PortServices.Time);
        }

        Assert.NotSame(scoped, await other);
    }

    private sealed class FixedTime : TimeProvider;

    private sealed class RecordingFactory : ILoggerFactory
    {
        public List<string> Categories { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
