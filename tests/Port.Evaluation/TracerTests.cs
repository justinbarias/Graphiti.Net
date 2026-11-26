// Tests for graphiti_core/tracer.py. Upstream has no test file for this module (tests/utils/search/test_search_tracing.py
// tests the search module with its own recording tracer, and waits for that unit). These tests hold the three results
// that a caller sees, as Python gave them (port/GAPS.md, U19), and the cases that the recording cannot hold: a source
// that no listener reads, and the form of the body with no result.
namespace Graphiti.Port.Evaluation;

using System.Diagnostics;
using Graphiti.Core;

public class TracerTests
{
    [Fact]
    public async Task a_body_that_returns_gives_its_result_with_the_open_telemetry_tracer()
    {
        using var probe = new SpanProbe();
        var tracer = TracerModule.CreateTracer(probe.Source, "pre.");

        var result = await tracer.StartSpanAsync("op", span =>
        {
            span.AddAttributes(new OrderedDictionary<string, object?> { ["a"] = 1L });
            return Task.FromResult(5L);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(5L, result);
        var span = Assert.Single(probe.Stopped);
        Assert.Equal("pre.op", span.DisplayName);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    // Python: RuntimeError: generator didn't stop after throw(), with ValueError('boom') as the context. The span has
    // the error status 'ValueError: boom' and one exception event.
    [Fact]
    public async Task a_body_that_raises_leaves_as_a_runtime_error_with_the_open_telemetry_tracer()
    {
        using var probe = new SpanProbe();
        var tracer = TracerModule.CreateTracer(probe.Source);
        var error = new ArgumentException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracer.StartSpanAsync<long>("op", _ => throw error, TestContext.Current.CancellationToken));

        Assert.Equal("generator didn't stop after throw()", thrown.Message);
        Assert.Same(error, thrown.InnerException);
        var span = Assert.Single(probe.Stopped);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("ArgumentException: boom", span.StatusDescription);
        var recorded = Assert.Single(span.Events);
        Assert.Equal("exception", recorded.Name);
    }

    // Python: ValueError('boom'), with no context.
    [Fact]
    public async Task a_body_that_raises_leaves_as_it_is_with_the_no_op_tracer()
    {
        var tracer = TracerModule.CreateTracer();
        Assert.IsType<NoOpTracer>(tracer);
        var error = new ArgumentException("boom");

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => tracer.StartSpanAsync<long>("op", _ => throw error, TestContext.Current.CancellationToken));

        Assert.Same(error, thrown);
        Assert.Null(thrown.InnerException);
    }

    // Python: a source that nobody reads is a span that nobody records, and the defect is the same.
    [Fact]
    public async Task a_source_with_no_listener_gives_the_result_and_the_same_defect()
    {
        using var source = new ActivitySource("tracer-test-no-listener-" + Guid.NewGuid().ToString("N"));
        var tracer = TracerModule.CreateTracer(source);

        var result = await tracer.StartSpanAsync("op", span =>
        {
            span.AddAttributes(new OrderedDictionary<string, object?> { ["a"] = "x" });
            span.SetStatus("error", "bad");
            span.RecordException(new ArgumentException("kept out"));
            return Task.FromResult("done");
        }, TestContext.Current.CancellationToken);
        Assert.Equal("done", result);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => tracer.StartSpanAsync("op", _ => throw new ArgumentException("boom"), TestContext.Current.CancellationToken));
        Assert.Equal("generator didn't stop after throw()", thrown.Message);
    }

    // Python: CancelledError is a BaseException, so `except Exception` does not catch it and it leaves as it is. The
    // span ends with no status and no event.
    [Fact]
    public async Task a_cancellation_leaves_as_it_is_and_the_span_ends_with_no_status()
    {
        using var probe = new SpanProbe();
        var tracer = TracerModule.CreateTracer(probe.Source);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            tracer.StartSpanAsync<long>("op", _ => throw new OperationCanceledException(cancellation.Token), cancellation.Token));

        Assert.Null(thrown.InnerException);
        var span = Assert.Single(probe.Stopped);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Empty(span.Events);
    }

    [Fact]
    public async Task the_form_with_no_result_runs_the_body_with_each_tracer()
    {
        using var probe = new SpanProbe();
        var seen = new List<string>();

        await TracerModule.CreateTracer(probe.Source).StartSpanAsync("a", span =>
        {
            seen.Add(span.GetType().Name);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        await TracerModule.CreateTracer().StartSpanAsync("b", span =>
        {
            seen.Add(span.GetType().Name);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["OpenTelemetrySpan", "NoOpSpan"], seen);
        Assert.Equal("graphiti.a", Assert.Single(probe.Stopped).DisplayName);
    }

    private sealed class SpanProbe : IDisposable
    {
        private readonly ActivityListener _listener;

        internal SpanProbe()
        {
            var name = "tracer-test-" + Guid.NewGuid().ToString("N");
            Source = new ActivitySource(name);
            _listener = new ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Stopped.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        internal ActivitySource Source { get; }

        internal List<Activity> Stopped { get; } = [];

        public void Dispose()
        {
            _listener.Dispose();
            Source.Dispose();
        }
    }
}
