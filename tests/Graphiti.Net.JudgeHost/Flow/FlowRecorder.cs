namespace Graphiti.JudgeHost.Flow;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.PyCompat;

/// <summary>
/// What one step sent across the seams: <c>Recorder</c> of <c>port/judge/host-python/judge_clients.py</c>. The seams
/// run on the thread pool and on the scheduler, so each list is guarded by one lock. A step entry keeps the lists of
/// its step, and the result encodes them at the end of the run, as <c>host.py</c> serializes them at the end.
/// </summary>
internal sealed partial class FlowRecorder
{
    private const string LabelRoot = "Entity";
    private const string JsonString = """
        "(?:[^"\\]|\\.)*"
        """;

    private const string ReprString = """
        '(?:[^'\\]|\\.)*'
        """;

    private readonly Lock gate = new();
    private readonly PyDateTime low;

    internal FlowRecorder()
    {
        var now = DateTimeOffset.UtcNow;
        Started = PyDateTime.FromDateTimeOffset(now);
        low = PyDateTime.FromDateTimeOffset(now.AddSeconds(-2));
        Reset();
    }

    /// <summary>Gets <c>datetime.now(timezone.utc)</c> at the start of the run.</summary>
    internal PyDateTime Started { get; }

    /// <summary>Gets the requests of this step that had no stored reply.</summary>
    internal List<JsonNode> Missing { get; private set; } = [];

    private StepCapture Current { get; set; } = new();

    /// <summary>Starts the lists of a new step and returns them.</summary>
    internal StepCapture Reset()
    {
        lock (gate)
        {
            Current = new StepCapture();
            Missing = [];
            return Current;
        }
    }

    internal void AddLlm(JsonNode entry)
    {
        lock (gate)
        {
            Current.Llm.Add(entry);
        }
    }

    internal void AddMissing(JsonNode entry)
    {
        lock (gate)
        {
            Missing.Add(entry);
        }
    }

    internal void AddEmbed(JsonNode entry)
    {
        lock (gate)
        {
            Current.Embed.Add(entry);
        }
    }

    internal void AddRerank(JsonNode entry)
    {
        lock (gate)
        {
            Current.Rerank.Add(entry);
        }
    }

    internal QueryUnit AddQuery(QueryUnit unit)
    {
        lock (gate)
        {
            Current.Queries.Add(unit);
            return unit;
        }
    }

    internal void AddOp(QueryUnit unit, JsonObject op)
    {
        lock (gate)
        {
            unit.Ops!.Add(op);
        }
    }

    /// <summary><c>take()</c>: the four lists of a step. A session with no operation is left out.</summary>
    internal JsonObject Take(StepCapture capture)
    {
        lock (gate)
        {
            return new JsonObject
            {
                ["llm"] = new JsonArray([.. capture.Llm.Select(Copy)]),
                ["embed"] = new JsonArray([.. capture.Embed.Select(Copy)]),
                ["rerank"] = new JsonArray([.. capture.Rerank.Select(Copy)]),
                ["queries"] = new JsonArray([.. capture.Queries.Where(q => q.Ops is null ? q.Query.Length > 0 : q.Ops.Count > 0).Select(q => (JsonNode)q.ToJson())]),
            };
        }
    }

    /// <summary>
    /// <c>mask</c>: UUIDs become <c>&lt;uuid&gt;</c>, a timestamp at or after the start of the run less two seconds becomes
    /// <c>&lt;now&gt;</c>, and each label list is sorted. A reply key must not depend on these.
    /// </summary>
    internal string Mask(string text)
    {
        var noUuid = UuidPattern().Replace(text, "<uuid>");
        var noClock = IsoPattern().Replace(noUuid, match =>
        {
            PyDateTime value;
            try
            {
                value = PyDateTime.FromIsoFormat(match.Value.Replace("Z", "+00:00", StringComparison.Ordinal).Replace(' ', 'T'));
            }
            catch (ArgumentException)
            {
                return match.Value;
            }

            if (value.IsNaive)
            {
                value = value.ReplaceUtc();
            }

            return value >= low ? "<now>" : match.Value;
        });
        return LabelTextOrder(noClock);
    }

    /// <summary><c>label_text_order</c>: sorts each label list inside a text.</summary>
    internal static string LabelTextOrder(string text)
    {
        if (!text.Contains(LabelRoot, StringComparison.Ordinal))
        {
            return text;
        }

        var arrays = LabelArrayPattern().Replace(text, match =>
        {
            var items = LabelItemPattern().Matches(match.Value).Select(m => m.Value).ToList();
            if (!items.Any(item => item[1..^1] == LabelRoot))
            {
                return match.Value;
            }

            var ordered = items.Order(StringComparer.Ordinal).GetEnumerator();
            return LabelItemPattern().Replace(match.Value, _ =>
            {
                ordered.MoveNext();
                return ordered.Current;
            });
        });
        return LabelChainPattern().Replace(arrays, match =>
        {
            var parts = match.Value[1..].Split(':');
            return parts.Contains(LabelRoot) ? ":" + string.Join(':', parts.Order(StringComparer.Ordinal)) : match.Value;
        });
    }

    private static JsonNode Copy(JsonNode node) => node.DeepClone();

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f])")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex IsoPattern();

    [GeneratedRegex(@"\[\s*(?:" + JsonString + @"(?:\s*,\s*" + JsonString + @")+|" + ReprString + @"(?:\s*,\s*" + ReprString + @")+)\s*\]")]
    private static partial Regex LabelArrayPattern();

    [GeneratedRegex(JsonString + "|" + ReprString)]
    private static partial Regex LabelItemPattern();

    [GeneratedRegex("(?::[A-Za-z_][A-Za-z0-9_]*){2,}")]
    private static partial Regex LabelChainPattern();
}

/// <summary>The four capture lists of one step.</summary>
internal sealed class StepCapture
{
    internal List<JsonNode> Llm { get; } = [];

    internal List<JsonNode> Embed { get; } = [];

    internal List<JsonNode> Rerank { get; } = [];

    internal List<QueryUnit> Queries { get; } = [];
}

/// <summary>
/// One entry of <c>queries</c>: an <c>execute_query</c> call (<see cref="Ops"/> is null), or a session with its
/// operations in order.
/// </summary>
internal sealed class QueryUnit
{
    private QueryUnit(string query, JsonNode? parameters, JsonObject? options, List<JsonObject>? ops)
    {
        Query = query;
        Parameters = parameters;
        Options = options;
        Ops = ops;
    }

    internal string Query { get; }

    internal JsonNode? Parameters { get; }

    internal JsonObject? Options { get; }

    internal List<JsonObject>? Ops { get; }

    internal static QueryUnit ExecuteQuery(string query, JsonNode parameters, JsonObject? options) => new(query, parameters, options, null);

    internal static QueryUnit Session() => new(string.Empty, null, null, []);

    internal JsonObject ToJson()
    {
        if (Ops is not null)
        {
            return new JsonObject { ["kind"] = "session", ["ops"] = new JsonArray([.. Ops.Select(op => op.DeepClone())]) };
        }

        var unit = new JsonObject { ["kind"] = "query", ["q"] = Query, ["params"] = Parameters?.DeepClone() };
        if (Options is not null)
        {
            unit["options"] = Options.DeepClone();
        }

        return unit;
    }
}
