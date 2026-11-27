namespace Graphiti;

/// <summary>
/// A cross-encoder that ranks passages by their relevance to a query. It is the public form of upstream's
/// <c>CrossEncoderClient.rank</c>. Graphiti calls it on the thread pool.
/// </summary>
public interface ICrossEncoder
{
    /// <summary>Ranks <paramref name="passages"/> by their relevance to <paramref name="query"/>.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="passages">The passages to rank.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Each passage with its score, in descending order of the score.</returns>
    Task<IReadOnlyList<(string Passage, double Score)>> RankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default);
}
