namespace Graphiti.Internal;

using Graphiti.Core.CrossEncoder;

/// <summary>
/// Gives a host <see cref="ICrossEncoder"/> to ported code as a <see cref="CrossEncoderClient"/>. It calls the host
/// cross-encoder on the thread pool (decision D33) and returns its result in the order that the host gives.
/// </summary>
internal sealed class CrossEncoderBridge : CrossEncoderClient
{
    private readonly ICrossEncoder crossEncoder;

    /// <summary>Initializes a new instance of the <see cref="CrossEncoderBridge"/> class.</summary>
    /// <param name="crossEncoder">The host cross-encoder.</param>
    internal CrossEncoderBridge(ICrossEncoder crossEncoder)
    {
        ArgumentNullException.ThrowIfNull(crossEncoder);
        this.crossEncoder = crossEncoder;
    }

    /// <inheritdoc/>
    internal override async Task<List<(string, double)>> RankAsync(
        string query,
        List<string> passages,
        CancellationToken cancellationToken = default)
    {
        var info = new ModelCallInfo("rerank", null, null, null, null, null);
        var ranked = await GraphitiModelCall.CallHostAsync(
            info, token => crossEncoder.RankAsync(query, passages, token), cancellationToken);
        return ranked.Select(r => (r.Passage, r.Score)).ToList();
    }
}
