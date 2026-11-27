namespace Graphiti.Internal;

using System.Net;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// The ambient holder of decision D23. Ported code reads the logger, the clock, the random source,
/// and the failure classifier here, so that each ported signature stays equal to Python.
/// The values are process-wide. The facade sets them at the first resolution of <c>IGraphiti</c>.
/// A test never sets them: it calls <see cref="Use"/>, which replaces a value for its own flow of
/// execution only, so that tests that run at the same time do not see each other's value.
/// </summary>
internal static class PortServices
{
    // A value of the scope of Use wins over the process-wide value.
    private static readonly AsyncLocal<Scope?> Current = new();

    private static ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
    private static TimeProvider time = TimeProvider.System;
    private static PyRandom random = new();
    private static Func<Exception, ModelCallFailure> modelFailureClassifier = DefaultClassifyModelFailure;

    /// <summary>The factory behind <see cref="Logger"/>. The default writes nothing.</summary>
    internal static ILoggerFactory LoggerFactory
    {
        get => Current.Value?.LoggerFactory ?? loggerFactory;
        set => loggerFactory = value;
    }

    /// <summary>The clock of ported code: <c>datetime.now</c>, <c>asyncio.sleep</c>, and each timeout.</summary>
    internal static TimeProvider Time
    {
        get => Current.Value?.Time ?? time;
        set => time = value;
    }

    /// <summary>The random source of ported code (<c>random.*</c>, GAPS F9).</summary>
    internal static PyRandom Random
    {
        get => Current.Value?.Random ?? random;
        set => random = value;
    }

    /// <summary>
    /// The classifier of the option <c>Llm.ClassifyFailure</c> (decision D31).
    /// The default is <see cref="DefaultClassifyModelFailure"/>.
    /// </summary>
    internal static Func<Exception, ModelCallFailure> ModelFailureClassifier
    {
        get => Current.Value?.ModelFailureClassifier ?? modelFailureClassifier;
        set => modelFailureClassifier = value;
    }

    /// <summary>
    /// Replaces the given values for the current flow of execution, and for the tasks that it starts, until
    /// the result is disposed. A value that is null keeps the value of the scope around it. This is the
    /// seam of a test, as <c>PyEnv.Use</c> is for the environment.
    /// </summary>
    internal static IDisposable Use(
        ILoggerFactory? loggerFactory = null,
        TimeProvider? time = null,
        PyRandom? random = null,
        Func<Exception, ModelCallFailure>? modelFailureClassifier = null)
    {
        var previous = Current.Value;
        Current.Value = new Scope(
            loggerFactory ?? previous?.LoggerFactory,
            time ?? previous?.Time,
            random ?? previous?.Random,
            modelFailureClassifier ?? previous?.ModelFailureClassifier);
        return new Restore(previous);
    }

    /// <summary>
    /// The logger of a ported module, as <c>logging.getLogger(__name__)</c>. The category is the full
    /// name of the module class. It reads the factory at each call, so a module that was loaded
    /// before the facade set the factory still logs to it.
    /// </summary>
    internal static ILogger Logger(Type moduleClass) => LoggerFactory.CreateLogger(moduleClass);

    /// <summary>
    /// Says what kind of failure an exception from a model call is (rule R4). The classifier of the
    /// option runs first. If it says <see cref="ModelCallFailure.None"/>, the default classifier decides
    /// (decision D31).
    /// </summary>
    internal static ModelCallFailure ClassifyModelFailure(Exception error)
    {
        var failure = ModelFailureClassifier(error);
        return failure == ModelCallFailure.None ? DefaultClassifyModelFailure(error) : failure;
    }

    /// <summary>
    /// The default classifier. It knows <see cref="HttpRequestException"/> only: status 429 is a rate
    /// limit, and a status from 500 to 599 is a server error.
    /// </summary>
    internal static ModelCallFailure DefaultClassifyModelFailure(Exception error) => error switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => ModelCallFailure.RateLimit,
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError and <= (HttpStatusCode)599 } => ModelCallFailure.ServerError,
        _ => ModelCallFailure.None,
    };

    private sealed record Scope(
        ILoggerFactory? LoggerFactory,
        TimeProvider? Time,
        PyRandom? Random,
        Func<Exception, ModelCallFailure>? ModelFailureClassifier);

    private sealed class Restore(Scope? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
