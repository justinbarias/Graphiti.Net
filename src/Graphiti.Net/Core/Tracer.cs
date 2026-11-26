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
using System.Numerics;
using Graphiti.PyCompat;

// PORT: the module class of R2. The import of opentelemetry in try and except ImportError keeps the branch where the
// import works (R17). opentelemetry is System.Diagnostics in the library table of DECISIONS.md: a Span is an Activity,
// and an opentelemetry Tracer is an ActivitySource.
internal static partial class TracerModule
{
    internal const bool OtelAvailable = true;
}

/// <summary>Abstract base class for tracer spans.</summary>
// PORT: an abstract method has no body, as in Python.
internal abstract class TracerSpan
{
    /// <summary>Add attributes to the span.</summary>
    internal abstract void AddAttributes(OrderedDictionary<string, object?> attributes);

    /// <summary>Set the status of the span.</summary>
    internal abstract void SetStatus(string status, string? description = null);

    /// <summary>Record an exception in the span.</summary>
    internal abstract void RecordException(Exception exception);
}

/// <summary>Abstract base class for tracers.</summary>
// PORT: `with tracer.start_span(name) as span:` is `await tracer.StartSpanAsync(name, async span => { ... }, token)`
// (R16). The tracer takes the body, because the end of the Python context manager depends on the exception of the
// body (upstream defect U19, R21), and a Dispose call cannot see that exception.
internal abstract class Tracer
{
    /// <summary>Start a new span with the given name.</summary>
    internal abstract Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default);

    // PORT: the form for a body with no result. Python has one start_span for both. It has no ConfigureAwait(false),
    // because R8 bans it in ported code: it leaves the scheduler.
    internal async Task StartSpanAsync(
        string name,
        Func<TracerSpan, Task> body,
        CancellationToken cancellationToken = default)
    {
        await StartSpanAsync<object?>(
            name,
            async span =>
            {
                await body(span);
                return null;
            },
            cancellationToken);
    }
}

/// <summary>No-op span implementation that does nothing.</summary>
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

/// <summary>No-op tracer implementation that does nothing.</summary>
internal sealed class NoOpTracer : Tracer
{
    /// <summary>Return a no-op span.</summary>
    // PORT: the contextmanager has one yield, so the body runs with a NoOpSpan, and an exception of the body leaves as it is.
    internal override async Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        return await body(new NoOpSpan());
    }
}

/// <summary>Wrapper for OpenTelemetry span.</summary>
internal sealed class OpenTelemetrySpan : TracerSpan
{
    // PORT: ActivitySource.StartActivity returns null if no listener reads the source, so the span can be null. A
    // Python span that nobody records (NonRecordingSpan) does nothing. Each member below does nothing for a null span
    // and for a span with IsAllDataRequested false, which is the Activity that a sampler does not record.
    private readonly Activity? span;

    internal OpenTelemetrySpan(Activity? span)
    {
        this.span = span;
    }

    /// <summary>Add attributes to the OpenTelemetry span.</summary>
    internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
    {
        try
        {
            // Filter out None values and convert all values to appropriate types
            OrderedDictionary<string, object?> filteredAttrs = [];
            foreach (var (key, value) in attributes)
            {
                if (value is not null)
                {
                    // Convert to string if not a primitive type
                    if (value is string or long or BigInteger or double or bool)
                    {
                        filteredAttrs[key] = value;
                    }
                    else if (value is int)
                    {
                        // PORT: a C# int is outside the value model (R6). Python has no such value: `len(x)` is a Python int,
                        // and the port writes it as `(long)x.Count` (R11). The catch below would drop each attribute of the
                        // call with no sign, so this error of the caller leaves as it is.
                        throw new InvalidCastException("A span attribute of the type int is outside the value model (R6). Cast it to long.");
                    }
                    else
                    {
                        filteredAttrs[key] = Py.Str(value);
                    }
                }
            }

            if (Py.Truthy(filteredAttrs))
            {
                // PORT: set_attributes of opentelemetry is one set_attribute for each pair. The SDK drops an empty key and
                // keeps the other pairs (it logs: invalid attribute key, must be non-empty string, dropping key).
                foreach (var (key, value) in filteredAttrs)
                {
                    if (key.Length > 0 && span is { IsAllDataRequested: true })
                    {
                        span.SetTag(key, value);
                    }
                }
            }
        }
        catch (Exception e) when (e is not InvalidCastException)
        {
            // Silently ignore tracing errors
        }
    }

    /// <summary>Set the status of the OpenTelemetry span.</summary>
    internal override void SetStatus(string status, string? description = null)
    {
        try
        {
            // PORT: `if OTEL_AVAILABLE:` keeps its body (R17). The Span.set_status of the opentelemetry SDK ignores each call
            // after the status OK, and Activity.SetStatus does not. The test on Status is that rule of the SDK.
            if (status == "error")
            {
                if (span is { IsAllDataRequested: true, Status: not ActivityStatusCode.Ok })
                {
                    span.SetStatus(ActivityStatusCode.Error, description);
                }
            }
            else if (status == "ok")
            {
                if (span is { IsAllDataRequested: true, Status: not ActivityStatusCode.Ok })
                {
                    span.SetStatus(ActivityStatusCode.Ok, description);
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore tracing errors
        }
    }

    /// <summary>Record an exception in the OpenTelemetry span.</summary>
    internal override void RecordException(Exception exception)
    {
        try
        {
            // PORT: D46. The telemetry of an exception keeps its .NET type name. Python gives its own class name here.
            if (span is { IsAllDataRequested: true })
            {
                span.AddException(exception);
            }
        }
        catch (Exception)
        {
            // PORT: `with suppress(Exception):`
        }
    }
}

/// <summary>Wrapper for OpenTelemetry tracer with configurable span name prefix.</summary>
internal sealed class OpenTelemetryTracer : Tracer
{
    private readonly ActivitySource tracer;

    private readonly string spanPrefix;

    /// <summary>
    /// Initialize the OpenTelemetry tracer wrapper.
    ///
    /// Parameters
    /// ----------
    /// tracer : opentelemetry.trace.Tracer
    ///     The OpenTelemetry tracer instance.
    /// span_prefix : str, optional
    ///     Prefix to prepend to all span names. Defaults to 'graphiti'.
    /// </summary>
    // PORT: `if not OTEL_AVAILABLE: raise ImportError` has no branch, because the import works (R17).
    internal OpenTelemetryTracer(ActivitySource tracer, string spanPrefix = "graphiti")
    {
        this.tracer = tracer;
        this.spanPrefix = PyText.RStrip(spanPrefix, ".");
    }

    /// <summary>Start a new OpenTelemetry span with the configured prefix.</summary>
    // PORT: upstream defect U19. The generator wraps its yield in `try` and `except Exception`, and yields a second time
    // there. If the body raises, `contextlib` raises RuntimeError: generator didn't stop after throw(), with the
    // exception of the body as the context. This method reproduces it with InvalidOperationException and the exception
    // of the body as the inner exception. A cancellation is a BaseException in Python, and it leaves as it is.
    internal override async Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        var entered = false;
        Exception? bodyError = null;
        T result;
        try
        {
            var fullName = $"{spanPrefix}.{name}";

            // PORT: `with self._tracer.start_as_current_span(full_name) as span:`. The with of opentelemetry
            // (use_span) records the exception of the body and sets the error status of a recording span. It ends the
            // span in each case. An Activity has no such context manager, so it is written out here.
            using var span = tracer.StartActivity(fullName);
            entered = true;
            try
            {
                result = await body(new OpenTelemetrySpan(span));
            }
            catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
            {
                bodyError = e;
                if (span is { IsAllDataRequested: true })
                {
                    // PORT: D46. The telemetry of an exception keeps its .NET type name. Python gives its own class name here.
                    span.AddException(e);

                    // The SDK ignores a status call after the status OK, as in OpenTelemetrySpan.SetStatus.
                    if (span.Status != ActivityStatusCode.Ok)
                    {
                        // PORT: D46. The telemetry of an exception keeps its .NET type name. Python gives its own class name here.
                        span.SetStatus(ActivityStatusCode.Error, $"{e.GetType().Name}: {e.Message}");
                    }
                }

                throw;
            }
            catch (Exception e)
            {
                // PORT: a cancellation of the body is a BaseException in Python. It leaves the with, and the span ends with
                // no event and no status. If the end of the span then raises an Exception, the generator yields a second time
                // while the cancellation is thrown into it, and its context is that cancellation.
                bodyError = e;
                throw;
            }
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            // If tracing fails, yield a no-op span to prevent breaking the operation
            if (!entered)
            {
                return await body(new NoOpSpan());
            }

            // PORT: upstream defect U19. A second yield after the end of the body.
            throw bodyError is null
                ? new InvalidOperationException("generator didn't stop")
                : new InvalidOperationException("generator didn't stop after throw()", bodyError);
        }

        return result;
    }
}

internal static partial class TracerModule
{
    /// <summary>
    /// Create a tracer instance.
    ///
    /// Parameters
    /// ----------
    /// otel_tracer : opentelemetry.trace.Tracer | None, optional
    ///     An OpenTelemetry tracer instance. If None, a no-op tracer is returned.
    /// span_prefix : str, optional
    ///     Prefix to prepend to all span names. Defaults to 'graphiti'.
    ///
    /// Returns
    /// -------
    /// Tracer
    ///     A tracer instance (either OpenTelemetryTracer or NoOpTracer).
    ///
    /// Examples
    /// --------
    /// Using with OpenTelemetry:
    ///
    /// &gt;&gt;&gt; from opentelemetry import trace
    /// &gt;&gt;&gt; otel_tracer = trace.get_tracer(__name__)
    /// &gt;&gt;&gt; tracer = create_tracer(otel_tracer, span_prefix='myapp.graphiti')
    ///
    /// Using no-op tracer:
    ///
    /// &gt;&gt;&gt; tracer = create_tracer()  # Returns NoOpTracer
    /// </summary>
    internal static Tracer CreateTracer(ActivitySource? otelTracer = null, string spanPrefix = "graphiti")
    {
        if (otelTracer is null)
        {
            return new NoOpTracer();
        }

        // PORT: `if not OTEL_AVAILABLE: return NoOpTracer()` has no branch, because the import works (R17).
        return new OpenTelemetryTracer(otelTracer, spanPrefix);
    }
}
