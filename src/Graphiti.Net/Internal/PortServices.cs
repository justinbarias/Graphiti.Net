namespace Graphiti.Internal;

using System.Net;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// The ambient holder of decision D23. Ported code reads the logger, the clock, the random source,
/// and the failure classifier here, so that each ported signature stays equal to Python.
/// The values are process-wide. The facade sets them at the first resolution of <c>IGraphiti</c>,
/// and a test sets them before it calls ported code.
/// </summary>
internal static class PortServices
{
    /// <summary>The factory behind <see cref="Logger"/>. The default writes nothing.</summary>
    internal static ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>The clock of ported code: <c>datetime.now</c>, <c>asyncio.sleep</c>, and each timeout.</summary>
    internal static TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>The random source of ported code (<c>random.*</c>, GAPS F9).</summary>
    internal static PyRandom Random { get; set; } = new();

    /// <summary>
    /// The classifier of the option <c>Llm.ClassifyFailure</c> (decision D31).
    /// The default is <see cref="DefaultClassifyModelFailure"/>.
    /// </summary>
    internal static Func<Exception, ModelCallFailure> ModelFailureClassifier { get; set; } = DefaultClassifyModelFailure;

    /// <summary>
    /// The logger of a ported module, as <c>logging.getLogger(__name__)</c>. The category is the full
    /// name of the module class. It reads the factory at each call, so a module that was loaded
    /// before the facade set the factory still logs to it.
    /// </summary>
    internal static ILogger Logger(Type moduleClass) => LoggerFactory.CreateLogger(moduleClass);

    /// <summary>Says what kind of failure an exception from a model call is (rule R4).</summary>
    internal static ModelCallFailure ClassifyModelFailure(Exception error) => ModelFailureClassifier(error);

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
}
