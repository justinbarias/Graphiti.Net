namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core.Ingestion;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/ingestion/config.py</c>.</summary>
internal static class ConfigBindings
{
    private const string Module = "graphiti_core.ingestion.config";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddModel(Module + ":ConcurrentIngestionConfig", data => ConcurrentIngestionConfig.ModelValidate(data))
            .AddType<ConcurrentIngestionConfig>("ConcurrentIngestionConfig", m =>
            [
                new("window", m.Window),
                new("state_store", m.StateStore),
                new("lease_seconds", m.LeaseSeconds),
                new("write_margin_seconds", m.WriteMarginSeconds),
                new("poll_interval_seconds", m.PollIntervalSeconds),
                new("index_lag_seconds", m.IndexLagSeconds),
                new("max_apply_attempts", m.MaxApplyAttempts),
                new("enabled_for_group", m.EnabledForGroup),
                new("before_commit_write", m.BeforeCommitWrite),
                new("before_enrichment_write", m.BeforeEnrichmentWrite),
            ]);

        table.Pure.Add(Module + ":ConcurrentIngestionConfig", call => Construct(call, ConcurrentIngestionConfig.ModelValidate));
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
