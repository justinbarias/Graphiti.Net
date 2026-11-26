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
    // the error status 'ValueError: boom' and one exception event. The text of the status and the type of the event
    // are those of .NET (decision D46), and TracerValuesTests compares them with the recording through its table D46Names.
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
        var recorded = Assert.Single(span.Events);
        Assert.Equal("exception", recorded.Name);
    }

    // Python: a span of a sampler that drops it is a NonRecordingSpan. use_span tests span.is_recording() before it
    // records the exception, and each call on the span does nothing. The RuntimeError still leaves.
    [Fact]
    public async Task a_span_that_the_sampler_does_not_record_keeps_no_exception_no_status_and_no_attribute()
    {
        using var probe = new SpanProbe(sampling: ActivitySamplingResult.PropagationData);
        var tracer = TracerModule.CreateTracer(probe.Source);
        var error = new ArgumentException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracer.StartSpanAsync<long>(
                "op",
                span =>
                {
                    span.AddAttributes(new OrderedDictionary<string, object?> { ["group_id"] = "g" });
                    span.SetStatus("error", "d");
                    span.RecordException(error);
                    throw error;
                },
                TestContext.Current.CancellationToken));

        Assert.Same(error, thrown.InnerException);
        var stopped = Assert.Single(probe.Stopped);
        Assert.False(stopped.IsAllDataRequested);
        Assert.Equal(ActivityStatusCode.Unset, stopped.Status);
        Assert.Empty(stopped.Events);
        Assert.Empty(stopped.TagObjects);
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

    // Python: the one start_span serves both forms, and the end of the span waits for the body. A body that raises under the
    // no-op tracer leaves as it is, and under the OpenTelemetry tracer it leaves as RuntimeError with the body error as
    // the context. The span records the error, so the span ended after the body.
    [Fact]
    public async Task the_form_with_no_result_waits_for_the_body_and_lets_its_exception_leave()
    {
        var finished = false;
        var error = new ArgumentException("x");

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => TracerModule.CreateTracer().StartSpanAsync("op", async _ =>
        {
            await Task.Yield();
            finished = true;
            throw error;
        }, TestContext.Current.CancellationToken));
        Assert.Same(error, thrown);
        Assert.True(finished);

        using var probe = new SpanProbe();
        finished = false;
        var wrapped = await Assert.ThrowsAsync<InvalidOperationException>(() => TracerModule.CreateTracer(probe.Source).StartSpanAsync("op", async _ =>
        {
            await Task.Yield();
            finished = true;
            throw error;
        }, TestContext.Current.CancellationToken));
        Assert.Same(error, wrapped.InnerException);
        Assert.True(finished);
        Assert.Equal(ActivityStatusCode.Error, Assert.Single(probe.Stopped).Status);
    }

    // Python: CancelledError is a BaseException, so it leaves as it is from the form with no result too. The token of the
    // call is what tells a cancellation of the caller from a failure of the body.
    [Fact]
    public async Task the_form_with_no_result_passes_its_token_so_that_a_cancellation_leaves_as_it_is()
    {
        using var probe = new SpanProbe();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TracerModule.CreateTracer(probe.Source).StartSpanAsync("op", _ => throw new OperationCanceledException(cancellation.Token), cancellation.Token));

        Assert.Null(thrown.InnerException);
        Assert.Equal(ActivityStatusCode.Unset, Assert.Single(probe.Stopped).Status);
    }

    // Python: if the end of the span raises after a body that returned, the generator yields a second time, and contextlib
    // raises RuntimeError: generator didn't stop, with no context (U19). The recording holds the same case with a span
    // processor that raises (tracer kind otel_fail_end). This test names the exact text and the missing inner exception.
    [Fact]
    public async Task a_failing_end_of_the_span_after_a_normal_body_leaves_as_a_runtime_error_with_no_context()
    {
        using var probe = new SpanProbe(stopped: _ => throw new ArgumentException("end failed"));
        var tracer = TracerModule.CreateTracer(probe.Source);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracer.StartSpanAsync("op", _ => Task.FromResult(5L), TestContext.Current.CancellationToken));

        Assert.Equal("generator didn't stop", thrown.Message);
        Assert.Null(thrown.InnerException);
    }

    // The value model has no C# int (R6). A caller passes `(long)x.Count`. An int leaves as InvalidCastException, where a
    // silent drop would lose each attribute of the call. NoOpSpan does nothing for it, as in Python.
    [Fact]
    public void an_attribute_of_the_type_int_leaves_as_an_invalid_cast_and_the_no_op_span_ignores_it()
    {
        var attributes = new OrderedDictionary<string, object?> { ["group_id"] = "g", ["node.count"] = 3 };

        Assert.Throws<InvalidCastException>(() => new OpenTelemetrySpan(null).AddAttributes(attributes));
        new NoOpSpan().AddAttributes(attributes);

        using var probe = new SpanProbe();
        using var activity = probe.Source.StartActivity("op");
        new OpenTelemetrySpan(activity).AddAttributes(new OrderedDictionary<string, object?> { ["group_id"] = "g", ["node.count"] = 3L, [string.Empty] = "dropped" });
        Assert.Equal(["group_id", "node.count"], activity!.TagObjects.Select(tag => tag.Key));
    }

    private sealed class SpanProbe : IDisposable
    {
        private readonly ActivityListener _listener;

        internal SpanProbe(Action<Activity>? stopped = null, ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded)
        {
            var name = "tracer-test-" + Guid.NewGuid().ToString("N");
            Source = new ActivitySource(name);
            _listener = new ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
                ActivityStopped = stopped ?? Stopped.Add,
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
