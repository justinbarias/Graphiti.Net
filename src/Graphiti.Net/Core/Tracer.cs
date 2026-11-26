// Ported from graphiti_core/tracer.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core;

using System.Diagnostics;

// PORT: the module class of R2. The import of opentelemetry in try and except ImportError keeps the branch where the
// import works (R17). opentelemetry is System.Diagnostics in the library table of DECISIONS.md: a Span is an Activity,
// and an opentelemetry Tracer is an ActivitySource.
internal static partial class TracerModule
{
    internal const bool OtelAvailable = true;
}

// PORT: an abstract method has no body, as in Python.
internal abstract class TracerSpan
{
    internal abstract void AddAttributes(OrderedDictionary<string, object?> attributes);

    internal abstract void SetStatus(string status, string? description = null);

    internal abstract void RecordException(Exception exception);
}

// PORT: `with tracer.start_span(name) as span:` is `await tracer.StartSpanAsync(name, async span => { ... }, token)`
// (R16). The tracer takes the body, because the end of the Python context manager depends on the exception of the
// body (upstream defect U19, R21), and a Dispose call cannot see that exception.
internal abstract class Tracer
{
    internal abstract Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default);

    // PORT: the form for a body with no result. Python has one start_span for both.
    internal async Task StartSpanAsync(
        string name,
        Func<TracerSpan, Task> body,
        CancellationToken cancellationToken = default)
    {
        await StartSpanAsync<object?>(
            name,
            async span =>
            {
                await body(span).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class NoOpSpan : TracerSpan
{
    internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
    {
    }

    internal override void SetStatus(string status, string? description = null)
    {
    }

    internal override void RecordException(Exception exception)
    {
    }
}

internal sealed class NoOpTracer : Tracer
{
    // TODO(port): NoOpTracer.start_span
    internal override Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NoOpTracer.start_span");
    }
}

internal sealed class OpenTelemetrySpan : TracerSpan
{
    // TODO(port): OpenTelemetrySpan.__init__
    // PORT: ActivitySource.StartActivity returns null if no listener reads the source, so the span can be null.
    internal OpenTelemetrySpan(Activity? span)
    {
        throw new NotSupportedException("TODO(port): OpenTelemetrySpan.__init__");
    }

    // TODO(port): OpenTelemetrySpan.add_attributes
    internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
    {
        throw new NotSupportedException("TODO(port): OpenTelemetrySpan.add_attributes");
    }

    // TODO(port): OpenTelemetrySpan.set_status
    internal override void SetStatus(string status, string? description = null)
    {
        throw new NotSupportedException("TODO(port): OpenTelemetrySpan.set_status");
    }

    // TODO(port): OpenTelemetrySpan.record_exception
    internal override void RecordException(Exception exception)
    {
        throw new NotSupportedException("TODO(port): OpenTelemetrySpan.record_exception");
    }
}

internal sealed class OpenTelemetryTracer : Tracer
{
    // TODO(port): OpenTelemetryTracer.__init__
    internal OpenTelemetryTracer(ActivitySource tracer, string spanPrefix = "graphiti")
    {
        throw new NotSupportedException("TODO(port): OpenTelemetryTracer.__init__");
    }

    // TODO(port): OpenTelemetryTracer.start_span
    internal override Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): OpenTelemetryTracer.start_span");
    }
}

internal static partial class TracerModule
{
    // TODO(port): create_tracer
    internal static Tracer CreateTracer(ActivitySource? otelTracer = null, string spanPrefix = "graphiti")
    {
        throw new NotSupportedException("TODO(port): create_tracer");
    }
}
