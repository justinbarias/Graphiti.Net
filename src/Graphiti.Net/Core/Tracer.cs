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
    // PORT: the contextmanager has one yield, so the body runs with a NoOpSpan, and an exception of the body leaves as it is.
    internal override async Task<T> StartSpanAsync<T>(
        string name,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        return await body(new NoOpSpan());
    }
}

internal sealed class OpenTelemetrySpan : TracerSpan
{
    // PORT: ActivitySource.StartActivity returns null if no listener reads the source, so the span can be null. A
    // Python span that nobody records does nothing, and each member below does nothing for a null span.
    private readonly Activity? _span;

    internal OpenTelemetrySpan(Activity? span)
    {
        _span = span;
    }

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
                    else
                    {
                        filteredAttrs[key] = Py.Str(value);
                    }
                }
            }

            if (Py.Truthy(filteredAttrs))
            {
                // PORT: set_attributes of opentelemetry is one set_attribute for each pair.
                foreach (var (key, value) in filteredAttrs)
                {
                    _span?.SetTag(key, value);
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore tracing errors
        }
    }

    internal override void SetStatus(string status, string? description = null)
    {
        try
        {
            // PORT: `if OTEL_AVAILABLE:` keeps its body (R17). The Span.set_status of the opentelemetry SDK ignores each call
            // after the status OK, and Activity.SetStatus does not. The test on Status is that rule of the SDK.
            if (status == "error")
            {
                if (_span is { Status: not ActivityStatusCode.Ok })
                {
                    _span.SetStatus(ActivityStatusCode.Error, description);
                }
            }
            else if (status == "ok")
            {
                if (_span is { Status: not ActivityStatusCode.Ok })
                {
                    _span.SetStatus(ActivityStatusCode.Ok, description);
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore tracing errors
        }
    }

    internal override void RecordException(Exception exception)
    {
        try
        {
            _span?.AddException(exception);
        }
        catch (Exception)
        {
            // PORT: `with suppress(Exception):`
        }
    }
}

internal sealed class OpenTelemetryTracer : Tracer
{
    private readonly ActivitySource _tracer;

    private readonly string _spanPrefix;

    // PORT: `if not OTEL_AVAILABLE: raise ImportError` has no branch, because the import works (R17).
    internal OpenTelemetryTracer(ActivitySource tracer, string spanPrefix = "graphiti")
    {
        _tracer = tracer;
        _spanPrefix = PyText.RStrip(spanPrefix, ".");
    }

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
            var fullName = $"{_spanPrefix}.{name}";

            // PORT: `with self._tracer.start_as_current_span(full_name) as span:`. The with of opentelemetry
            // (use_span) records the exception of the body and sets the error status of a recording span. It ends the
            // span in each case. An Activity has no such context manager, so it is written out here.
            using var span = _tracer.StartActivity(fullName);
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
                    span.AddException(e);

                    // The SDK ignores a status call after the status OK, as in OpenTelemetrySpan.SetStatus.
                    if (span.Status != ActivityStatusCode.Ok)
                    {
                        span.SetStatus(ActivityStatusCode.Error, $"{e.GetType().Name}: {e.Message}");
                    }
                }

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
