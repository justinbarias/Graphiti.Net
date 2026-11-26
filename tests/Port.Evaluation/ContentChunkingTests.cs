// Ported from tests/utils/test_content_chunking.py (port/RULES.md, R25). The test names are the Python names.
//
// TODO(port): ten tests are not ported, because each one sets a module constant with monkeypatch.setattr
// (content_chunking.CHUNK_DENSITY_THRESHOLD or CHUNK_MIN_TOKENS). RULES.md R17 makes these constants
// static readonly fields of Helpers with no seam for a test, so a test cannot change them:
//   TestShouldChunk: test_short_content_never_chunks, test_high_density_large_json_chunks,
//     test_low_density_text_no_chunk, test_low_density_json_no_chunk
//   TestJsonDensityEstimation: test_dense_array_detected, test_sparse_array_not_dense, test_dense_object_detected
//   TestTextDensityEstimation: test_entity_rich_text_detected, test_prose_not_dense, test_sentence_starters_ignored
// The judge cases of should_chunk, _json_likely_dense, and _text_likely_dense cover these paths with the default constants.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

internal static class ChunkingTestSupport
{
    internal static OrderedDictionary<string, object?> Dict(params (string Key, object? Value)[] entries)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in entries)
        {
            result[key] = value;
        }

        return result;
    }

    internal static string Dumps(object? value) => PyJson.Dumps(value);

    internal static List<object?> ParseList(string chunk) => (List<object?>)PyJson.Loads(chunk)!;

    internal static OrderedDictionary<string, object?> ParseDict(string chunk) => (OrderedDictionary<string, object?>)PyJson.Loads(chunk)!;

    internal static HashSet<(long, long)> CoveredPairs<T>(List<(List<T>, List<long>)> result)
    {
        HashSet<(long, long)> covered = [];
        foreach (var (_, indices) in result)
        {
            for (var i = 0; i < indices.Count; i++)
            {
                for (var j = i + 1; j < indices.Count; j++)
                {
                    covered.Add((Math.Min(indices[i], indices[j]), Math.Max(indices[i], indices[j])));
                }
            }
        }

        return covered;
    }

    internal static HashSet<(long, long)> AllPairs(int n)
    {
        HashSet<(long, long)> expected = [];
        for (long i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                expected.Add((i, j));
            }
        }

        return expected;
    }

    // random.seed(seed): the port seeds the ambient random source (R18).
    internal static T Seeded<T>(long seed, Func<T> body)
    {
        var saved = PortServices.Random;
        PortServices.Random = new PyRandom(seed);
        try
        {
            return body();
        }
        finally
        {
            PortServices.Random = saved;
        }
    }
}

public class TestEstimateTokens
{
    [Fact]
    public void test_empty_string()
    {
        Assert.Equal(0, ContentChunking.EstimateTokens(string.Empty));
    }

    [Fact]
    public void test_short_string()
    {
        // 4 chars per token
        Assert.Equal(1, ContentChunking.EstimateTokens("abcd"));
        Assert.Equal(2, ContentChunking.EstimateTokens("abcdefgh"));
    }

    [Fact]
    public void test_long_string()
    {
        var text = new string('a', 400);
        Assert.Equal(100, ContentChunking.EstimateTokens(text));
    }

    [Fact]
    public void test_uses_chars_per_token_constant()
    {
        var text = new string('x', (int)(ContentChunking.CharsPerToken * 10));
        Assert.Equal(10, ContentChunking.EstimateTokens(text));
    }
}

public class TestChunkJsonArray
{
    [Fact]
    public void test_small_array_no_chunking()
    {
        var data = new List<object?> { ChunkingTestSupport.Dict(("name", "Alice")), ChunkingTestSupport.Dict(("name", "Bob")) };
        var content = ChunkingTestSupport.Dumps(data);
        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 1000);
        Assert.Single(chunks);
        Assert.Equal(content, ChunkingTestSupport.Dumps(ChunkingTestSupport.ParseList(chunks[0])));
    }

    [Fact]
    public void test_empty_array()
    {
        var chunks = ContentChunking.ChunkJsonContent("[]", chunkSizeTokens: 100);
        Assert.Equal(["[]"], chunks);
    }

    [Fact]
    public void test_array_splits_at_element_boundaries()
    {
        // Create array that exceeds chunk size
        var data = Enumerable.Range(0, 20).Select(i => (object?)ChunkingTestSupport.Dict(("id", (long)i), ("data", new string('x', 100)))).ToList();
        var content = ChunkingTestSupport.Dumps(data);

        // Use small chunk size to force splitting
        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 100, overlapTokens: 20);

        // Verify all chunks are valid JSON arrays
        foreach (var chunk in chunks)
        {
            var parsed = ChunkingTestSupport.ParseList(chunk);

            // Each element should be a complete object
            foreach (var item in parsed)
            {
                var obj = Assert.IsType<OrderedDictionary<string, object?>>(item);
                Assert.True(obj.ContainsKey("id"));
                Assert.True(obj.ContainsKey("data"));
            }
        }
    }

    [Fact]
    public void test_array_preserves_all_elements()
    {
        var data = Enumerable.Range(0, 10).Select(i => (object?)ChunkingTestSupport.Dict(("id", (long)i))).ToList();
        var content = ChunkingTestSupport.Dumps(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        // Collect all unique IDs across chunks (accounting for overlap)
        HashSet<long> seenIds = [];
        foreach (var chunk in chunks)
        {
            foreach (var item in ChunkingTestSupport.ParseList(chunk))
            {
                seenIds.Add((long)((OrderedDictionary<string, object?>)item!)["id"]!);
            }
        }

        // All original IDs should be present
        Assert.True(seenIds.SetEquals(Enumerable.Range(0, 10).Select(i => (long)i)));
    }
}

public class TestChunkJsonObject
{
    [Fact]
    public void test_small_object_no_chunking()
    {
        var data = ChunkingTestSupport.Dict(("name", "Alice"), ("age", 30L));
        var content = ChunkingTestSupport.Dumps(data);
        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 1000);
        Assert.Single(chunks);
        Assert.Equal(content, ChunkingTestSupport.Dumps(ChunkingTestSupport.ParseDict(chunks[0])));
    }

    [Fact]
    public void test_empty_object()
    {
        var chunks = ContentChunking.ChunkJsonContent("{}", chunkSizeTokens: 100);
        Assert.Equal(["{}"], chunks);
    }

    [Fact]
    public void test_object_splits_at_key_boundaries()
    {
        // Create object that exceeds chunk size
        var data = new OrderedDictionary<string, object?>();
        for (var i = 0; i < 20; i++)
        {
            data[$"key_{i}"] = new string('x', 100);
        }

        var content = ChunkingTestSupport.Dumps(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 100, overlapTokens: 20);

        // Verify all chunks are valid JSON objects
        foreach (var chunk in chunks)
        {
            var parsed = ChunkingTestSupport.ParseDict(chunk);

            // Each key-value pair should be complete
            foreach (var key in parsed.Keys)
            {
                Assert.StartsWith("key_", key, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void test_object_preserves_all_keys()
    {
        var data = new OrderedDictionary<string, object?>();
        for (var i = 0; i < 10; i++)
        {
            data[$"key_{i}"] = $"value_{i}";
        }

        var content = ChunkingTestSupport.Dumps(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        // Collect all unique keys across chunks
        HashSet<string> seenKeys = [];
        foreach (var chunk in chunks)
        {
            seenKeys.UnionWith(ChunkingTestSupport.ParseDict(chunk).Keys);
        }

        // All original keys should be present
        var expectedKeys = Enumerable.Range(0, 10).Select(i => $"key_{i}");
        Assert.True(seenKeys.SetEquals(expectedKeys));
    }
}

public class TestChunkJsonInvalid
{
    [Fact]
    public void test_invalid_json_falls_back_to_text()
    {
        var invalidJson = "not valid json {";
        var chunks = ContentChunking.ChunkJsonContent(invalidJson, chunkSizeTokens: 1000);

        // Should fall back to text chunking
        Assert.True(chunks.Count >= 1);
        Assert.Contains(invalidJson, chunks[0], StringComparison.Ordinal);
    }

    [Fact]
    public void test_scalar_value_returns_as_is()
    {
        foreach (var scalar in new[] { "\"string\"", "123", "true", "null" })
        {
            var chunks = ContentChunking.ChunkJsonContent(scalar, chunkSizeTokens: 1000);
            Assert.Equal([scalar], chunks);
        }
    }
}

public class TestChunkTextContent
{
    [Fact]
    public void test_small_text_no_chunking()
    {
        var text = "This is a short text.";
        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 1000);
        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void test_splits_at_paragraph_boundaries()
    {
        var paragraphs = new[] { "Paragraph one.", "Paragraph two.", "Paragraph three." };
        var text = string.Join("\n\n", paragraphs);

        // Use small chunk size to force splitting
        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 10, overlapTokens: 5);

        // Each chunk should contain complete paragraphs (possibly with overlap)
        foreach (var chunk in chunks)
        {
            // Should not have partial words cut off mid-paragraph
            Assert.False(chunk.EndsWith(' '));
        }
    }

    [Fact]
    public void test_splits_at_sentence_boundaries_for_large_paragraphs()
    {
        // Create a single long paragraph with multiple sentences
        var sentences = Enumerable.Range(0, 20).Select(i => "This is sentence number " + i + ".");
        var longParagraph = string.Join(" ", sentences);

        var chunks = ContentChunking.ChunkTextContent(longParagraph, chunkSizeTokens: 50, overlapTokens: 10);

        // Should have multiple chunks
        Assert.True(chunks.Count > 1);

        // Each chunk should end at a sentence boundary where possible
        // (the Python assertion is "chunk[-1] in '.!? ' or True", which always holds)
    }

    [Fact]
    public void test_preserves_text_completeness()
    {
        var text = "Alpha beta gamma delta epsilon zeta eta theta.";
        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 10, overlapTokens: 2);

        // All words should appear in at least one chunk
        var allWords = PyText.Split(text.Replace(".", string.Empty, StringComparison.Ordinal)).ToHashSet();
        HashSet<string> foundWords = [];
        foreach (var chunk in chunks)
        {
            foundWords.UnionWith(PyText.Split(chunk.Replace(".", string.Empty, StringComparison.Ordinal)));
        }

        Assert.True(allWords.IsSubsetOf(foundWords));
    }
}

public class TestChunkMessageContent
{
    [Fact]
    public void test_small_message_no_chunking()
    {
        var content = "Alice: Hello!\nBob: Hi there!";
        var chunks = ContentChunking.ChunkMessageContent(content, chunkSizeTokens: 1000);
        Assert.Single(chunks);
        Assert.Equal(content, chunks[0]);
    }

    [Fact]
    public void test_preserves_speaker_message_format()
    {
        var messages = Enumerable.Range(0, 10).Select(i => $"Speaker{i}: This is message number {i}.");
        var content = string.Join("\n", messages);

        var chunks = ContentChunking.ChunkMessageContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        // Each chunk should have complete speaker:message pairs
        foreach (var chunk in chunks)
        {
            var lines = PyText.Split(chunk, "\n").Where(line => PyText.Strip(line).Length > 0);
            foreach (var line in lines)
            {
                // Should have speaker: format
                Assert.Contains(':', line);
            }
        }
    }

    [Fact]
    public void test_json_message_array_format()
    {
        var messages = Enumerable.Range(0, 10).Select(i => (object?)ChunkingTestSupport.Dict(("role", "user"), ("content", $"Message {i}"))).ToList();
        var content = ChunkingTestSupport.Dumps(messages);

        var chunks = ContentChunking.ChunkMessageContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        // Each chunk should be valid JSON array
        foreach (var chunk in chunks)
        {
            var parsed = ChunkingTestSupport.ParseList(chunk);
            foreach (var msg in parsed)
            {
                var obj = Assert.IsType<OrderedDictionary<string, object?>>(msg);
                Assert.True(obj.ContainsKey("role"));
                Assert.True(obj.ContainsKey("content"));
            }
        }
    }
}

public class TestChunkOverlap
{
    [Fact]
    public void test_json_array_overlap_captures_boundary_elements()
    {
        var data = Enumerable.Range(0, 10).Select(i => (object?)ChunkingTestSupport.Dict(("id", (long)i), ("name", $"Entity {i}"))).ToList();
        var content = ChunkingTestSupport.Dumps(data);

        // Use settings that will create overlap
        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 80, overlapTokens: 30);

        if (chunks.Count > 1)
        {
            // Check that adjacent chunks share some elements
            for (var i = 0; i < chunks.Count - 1; i++)
            {
                var current = ChunkingTestSupport.ParseList(chunks[i]);
                var nextChunk = ChunkingTestSupport.ParseList(chunks[i + 1]);

                // Get IDs from end of current and start of next
                var currentIds = current.Select(item => (long)((OrderedDictionary<string, object?>)item!)["id"]!).ToHashSet();
                var nextIds = nextChunk.Select(item => (long)((OrderedDictionary<string, object?>)item!)["id"]!).ToHashSet();

                // There should be overlap (shared IDs)
                // Note: overlap may be empty if elements are large
                // The test verifies the structure, not exact overlap amount
                currentIds.IntersectWith(nextIds);
            }
        }
    }

    [Fact]
    public void test_text_overlap_captures_boundary_text()
    {
        var paragraphs = Enumerable.Range(0, 10).Select(i => $"Paragraph {i} with some content here.");
        var text = string.Join("\n\n", paragraphs);

        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 50, overlapTokens: 20);

        if (chunks.Count > 1)
        {
            // Adjacent chunks should have some shared content
            for (var i = 0; i < chunks.Count - 1; i++)
            {
                var currentWords = PyText.Split(chunks[i]).ToHashSet();
                var nextWords = PyText.Split(chunks[i + 1]).ToHashSet();

                // There should be some overlap
                currentWords.IntersectWith(nextWords);

                // At minimum, common words like 'Paragraph', 'with', etc.
                Assert.True(currentWords.Count > 0);
            }
        }
    }
}

public class TestEdgeCases
{
    [Fact]
    public void test_very_large_single_element()
    {
        // Single element larger than chunk size
        var data = new List<object?> { ChunkingTestSupport.Dict(("content", new string('x', 10000))) };
        var content = ChunkingTestSupport.Dumps(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 100, overlapTokens: 10);

        // Should handle gracefully - may return single chunk or fall back
        Assert.True(chunks.Count >= 1);
    }

    [Fact]
    public void test_empty_content()
    {
        Assert.Equal([string.Empty], ContentChunking.ChunkTextContent(string.Empty, chunkSizeTokens: 100));
        Assert.Equal([string.Empty], ContentChunking.ChunkMessageContent(string.Empty, chunkSizeTokens: 100));
    }

    [Fact]
    public void test_whitespace_only()
    {
        var chunks = ContentChunking.ChunkTextContent("   \n\n   ", chunkSizeTokens: 100);
        Assert.True(chunks.Count >= 1);
    }
}

public class TestShouldChunk
{
    [Fact]
    public void test_empty_content_never_chunks()
    {
        // Empty content should never chunk.
        Assert.False(ContentChunking.ShouldChunk(string.Empty, EpisodeType.Text));
        Assert.False(ContentChunking.ShouldChunk(string.Empty, EpisodeType.Json));
    }
}

public class TestJsonDensityEstimation
{
    [Fact]
    public void test_count_json_keys_shallow()
    {
        // Key counting should work for nested structures.
        var data = ChunkingTestSupport.Dict(
            ("a", 1L),
            ("b", ChunkingTestSupport.Dict(("c", 2L), ("d", 3L))),
            ("e", new List<object?> { ChunkingTestSupport.Dict(("f", 4L)), ChunkingTestSupport.Dict(("g", 5L)) }));

        // At depth 2: a, b, c, d, e, f, g = 7 keys
        Assert.Equal(7, ContentChunking.CountJsonKeys(data, maxDepth: 2));
    }

    [Fact]
    public void test_count_json_keys_depth_limit()
    {
        // Key counting should respect depth limit.
        var data = ChunkingTestSupport.Dict(
            ("a", ChunkingTestSupport.Dict(("b", ChunkingTestSupport.Dict(("c", ChunkingTestSupport.Dict(("d", 1L))))))));

        // At depth 1: only 'a'
        Assert.Equal(1, ContentChunking.CountJsonKeys(data, maxDepth: 1));

        // At depth 2: 'a' and 'b'
        Assert.Equal(2, ContentChunking.CountJsonKeys(data, maxDepth: 2));
    }
}

public class TestGenerateCoveringChunks
{
    [Fact]
    public void test_empty_list()
    {
        // Empty list should return single chunk with empty items.
        var result = ContentChunking.GenerateCoveringChunks(new List<string>(), k: 3);

        // n=0 <= k=3, so returns single chunk with empty items
        var only = Assert.Single(result);
        Assert.Empty(only.Item1);
        Assert.Empty(only.Item2);
    }

    [Fact]
    public void test_single_item()
    {
        // Single item should return one chunk with that item.
        List<string> items = ["A"];
        var result = ContentChunking.GenerateCoveringChunks(items, k: 3);
        var only = Assert.Single(result);
        Assert.Equal(["A"], only.Item1);
        Assert.Equal([0L], only.Item2);
    }

    [Fact]
    public void test_items_fit_in_single_chunk()
    {
        // When n <= k, all items should be in one chunk.
        List<string> items = ["A", "B", "C"];
        var result = ContentChunking.GenerateCoveringChunks(items, k: 5);
        var (chunkItems, indices) = Assert.Single(result);
        Assert.Equal(items, chunkItems);
        Assert.Equal([0L, 1L, 2L], indices);
    }

    [Fact]
    public void test_items_equal_to_k()
    {
        // When n == k, all items should be in one chunk.
        List<string> items = ["A", "B", "C", "D"];
        var result = ContentChunking.GenerateCoveringChunks(items, k: 4);
        var (chunkItems, indices) = Assert.Single(result);
        Assert.Equal(items, chunkItems);
        Assert.Equal([0L, 1L, 2L, 3L], indices);
    }

    [Fact]
    public void test_all_pairs_covered_k2()
    {
        // With k=2, every pair of items must appear in exactly one chunk.
        List<string> items = ["A", "B", "C", "D"];
        var result = ContentChunking.GenerateCoveringChunks(items, k: 2);

        // Collect all pairs from chunks
        HashSet<(long, long)> coveredPairs = [];
        foreach (var (_, indices) in result)
        {
            Assert.Equal(2, indices.Count);
            coveredPairs.Add((Math.Min(indices[0], indices[1]), Math.Max(indices[0], indices[1])));
        }

        // All C(4,2) = 6 pairs should be covered
        Assert.True(coveredPairs.SetEquals(ChunkingTestSupport.AllPairs(4)));
    }

    [Fact]
    public void test_all_pairs_covered_k3()
    {
        // With k=3, every pair must appear in at least one chunk.
        var items = Enumerable.Range(0, 6).ToList(); // 0, 1, 2, 3, 4, 5
        var result = ContentChunking.GenerateCoveringChunks(items, k: 3);

        foreach (var (_, indices) in result)
        {
            Assert.Equal(3, indices.Count);
        }

        // All C(6,2) = 15 pairs should be covered
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(ChunkingTestSupport.AllPairs(6)));
    }

    [Fact]
    public void test_all_pairs_covered_larger()
    {
        // Verify all pairs covered for larger input.
        var items = Enumerable.Range(0, 10).ToList();
        var result = ContentChunking.GenerateCoveringChunks(items, k: 4);

        foreach (var (_, indices) in result)
        {
            Assert.Equal(4, indices.Count);
        }

        // All C(10,2) = 45 pairs should be covered
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(ChunkingTestSupport.AllPairs(10)));
    }

    [Fact]
    public void test_index_mapping_correctness()
    {
        // Global indices should correctly map to original items.
        List<string> items = ["Alice", "Bob", "Carol", "Dave", "Eve"];
        var result = ContentChunking.GenerateCoveringChunks(items, k: 3);

        foreach (var (chunkItems, indices) in result)
        {
            // Each chunk item should match the item at the corresponding global index
            for (var localIdx = 0; localIdx < indices.Count; localIdx++)
            {
                Assert.Equal(items[(int)indices[localIdx]], chunkItems[localIdx]);
            }
        }
    }

    [Fact]
    public void test_greedy_minimizes_chunks()
    {
        // Greedy approach should produce reasonably few chunks.
        var items = Enumerable.Range(0, 6).ToList();
        var result = ContentChunking.GenerateCoveringChunks(items, k: 3);

        // For small inputs (exhaustive enumeration), should achieve near-optimal
        // Should be at least the simple lower bound (5 for this case)
        Assert.True(result.Count >= 5);

        // Verify all pairs are covered (the primary guarantee)
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(ChunkingTestSupport.AllPairs(6)));
    }

    [Fact]
    public void test_works_with_custom_types()
    {
        // Function should work with any type, not just strings/ints.
        var items = new List<Entity> { new("A"), new("B"), new("C"), new("D") };
        var result = ContentChunking.GenerateCoveringChunks(items, k: 2);

        // Verify structure
        Assert.True(result.Count > 0);
        foreach (var (chunkItems, indices) in result)
        {
            Assert.Equal(2, chunkItems.Count);
            Assert.Equal(2, indices.Count);
        }
    }

    [Fact]
    public void test_deterministic_output()
    {
        // Same input should produce same output.
        var items = Enumerable.Range(0, 8).ToList();
        var result1 = ContentChunking.GenerateCoveringChunks(items, k: 3);
        var result2 = ContentChunking.GenerateCoveringChunks(items, k: 3);

        Assert.Equal(result1.Count, result2.Count);
        foreach (var ((chunk1, idx1), (chunk2, idx2)) in result1.Zip(result2))
        {
            Assert.Equal(chunk1, chunk2);
            Assert.Equal(idx1, idx2);
        }
    }

    [Fact]
    public void test_all_pairs_covered_k15_n30()
    {
        // Verify all pairs covered for n=30, k=15 (realistic edge extraction scenario).
        var n = 30;
        var k = 15;
        var items = Enumerable.Range(0, n).ToList();
        var result = ContentChunking.GenerateCoveringChunks(items, k: k);

        // Verify chunk sizes are at most k (fallback chunks may be smaller)
        foreach (var (_, indices) in result)
        {
            Assert.True(indices.Count <= k, $"Expected chunk size <= {k}, got {indices.Count}");
        }

        // All C(30,2) = 435 pairs should be covered
        var expectedPairs = ChunkingTestSupport.AllPairs(n);
        Assert.Equal(435, expectedPairs.Count);
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(expectedPairs));

        // Verify chunk count is at least the lower bound
        Assert.True(result.Count >= 5, $"Expected at least 5 chunks, got {result.Count}");
    }

    [Fact]
    public void test_all_pairs_covered_with_random_sampling()
    {
        // n=50, k=5 triggers sampling since C(50,5) = 2,118,760 > 1000
        var n = 50;
        var k = 5;
        var items = Enumerable.Range(0, n).ToList();

        // Test with multiple random seeds to ensure robustness
        for (var seed = 0; seed < 5; seed++)
        {
            var result = ChunkingTestSupport.Seeded(seed, () => ContentChunking.GenerateCoveringChunks(items, k: k));

            // All C(50,2) = 1225 pairs should be covered
            var expectedPairs = ChunkingTestSupport.AllPairs(n);
            Assert.Equal(1225, expectedPairs.Count);
            Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(expectedPairs), $"Seed {seed}: pairs are missing");
        }
    }

    [Fact]
    public void test_fallback_creates_pair_chunks_for_uncovered()
    {
        // Use a large n with small k to stress the sampling
        var n = 100;
        var k = 4;
        var items = Enumerable.Range(0, n).ToList();

        var result = ChunkingTestSupport.Seeded(42, () => ContentChunking.GenerateCoveringChunks(items, k: k));

        // All C(100,2) = 4950 pairs must be covered
        var expectedPairs = ChunkingTestSupport.AllPairs(n);
        Assert.Equal(4950, expectedPairs.Count);
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(expectedPairs));
    }

    [Fact]
    public void test_duplicate_sampling_safety()
    {
        // n=20, k=10: C(20,10) = 184,756 > 1000 triggers sampling
        // With large k relative to n, duplicates are more likely
        var n = 20;
        var k = 10;
        var items = Enumerable.Range(0, n).ToList();

        var result = ChunkingTestSupport.Seeded(123, () => ContentChunking.GenerateCoveringChunks(items, k: k));

        // All C(20,2) = 190 pairs should be covered
        var expectedPairs = ChunkingTestSupport.AllPairs(n);
        Assert.Equal(190, expectedPairs.Count);
        Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(expectedPairs));
    }

    [Fact]
    public void test_stress_multiple_seeds()
    {
        // The combination of greedy sampling and fallback logic should
        // guarantee all pairs are covered regardless of random seed.
        var n = 30;
        var k = 5;
        var items = Enumerable.Range(0, n).ToList();
        var expectedPairs = ChunkingTestSupport.AllPairs(n);

        for (var seed = 0; seed < 10; seed++)
        {
            var result = ChunkingTestSupport.Seeded(seed, () => ContentChunking.GenerateCoveringChunks(items, k: k));

            Assert.True(ChunkingTestSupport.CoveredPairs(result).SetEquals(expectedPairs), $"Seed {seed} failed to cover all pairs");
        }
    }

    private sealed record Entity(string Name);
}
