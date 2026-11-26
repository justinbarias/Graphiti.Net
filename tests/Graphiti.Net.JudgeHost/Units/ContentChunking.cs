namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/content_chunking.py</c>.</summary>
internal static class ContentChunkingBindings
{
    private const string Module = "graphiti_core.utils.content_chunking:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "estimate_tokens", call => call.Signature("text").InvokeAsync(ContentChunking.EstimateTokens));
        table.Pure.Add(Module + "_tokens_to_chars", call => call.Signature("tokens").InvokeAsync(ContentChunking.TokensToChars));
        table.Pure.Add(Module + "should_chunk", call => call.Signature("content", "episode_type").InvokeAsync(ContentChunking.ShouldChunk));
        table.Pure.Add(Module + "_estimate_high_density", call => call.Signature("content", "episode_type", "tokens").InvokeAsync(ContentChunking.EstimateHighDensity));
        table.Pure.Add(Module + "_json_likely_dense", call => call.Signature("content", "tokens").InvokeAsync(ContentChunking.JsonLikelyDense));
        table.Pure.Add(Module + "_count_json_keys", call => call.Signature("data", "max_depth", "current_depth").InvokeAsync(ContentChunking.CountJsonKeys));
        table.Pure.Add(Module + "_text_likely_dense", call => call.Signature("content", "tokens").InvokeAsync(ContentChunking.TextLikelyDense));
        table.Pure.Add(Module + "chunk_json_content", call => call.Signature("content", "chunk_size_tokens", "overlap_tokens").InvokeAsync(ContentChunking.ChunkJsonContent));
        table.Pure.Add(Module + "_chunk_json_array", call => call.Signature("data", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkJsonArray));
        table.Pure.Add(Module + "_get_overlap_elements", call => call.Signature("elements", "overlap_chars").InvokeAsync(ContentChunking.GetOverlapElements));
        table.Pure.Add(Module + "_chunk_json_object", call => call.Signature("data", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkJsonObject));
        table.Pure.Add(Module + "_get_overlap_dict", call => call.Signature("data", "keys", "overlap_chars").InvokeAsync(ContentChunking.GetOverlapDict));
        table.Pure.Add(Module + "chunk_text_content", call => call.Signature("content", "chunk_size_tokens", "overlap_tokens").InvokeAsync(ContentChunking.ChunkTextContent));
        table.Pure.Add(Module + "_chunk_by_sentences", call => call.Signature("text", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkBySentences));
        table.Pure.Add(Module + "_chunk_by_size", call => call.Signature("text", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkBySize));
        table.Pure.Add(Module + "_get_overlap_text", call => call.Signature("text", "overlap_chars").InvokeAsync(ContentChunking.GetOverlapText));
        table.Pure.Add(Module + "chunk_message_content", call => call.Signature("content", "chunk_size_tokens", "overlap_tokens").InvokeAsync(ContentChunking.ChunkMessageContent));
        table.Pure.Add(Module + "_chunk_message_array", call => call.Signature("messages", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkMessageArray));
        table.Pure.Add(Module + "_chunk_speaker_messages", call => call.Signature("content", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkSpeakerMessages));
        table.Pure.Add(Module + "_get_overlap_messages", call => call.Signature("messages", "overlap_chars").InvokeAsync(ContentChunking.GetOverlapMessages));
        table.Pure.Add(Module + "_chunk_by_lines", call => call.Signature("content", "chunk_size_chars", "overlap_chars").InvokeAsync(ContentChunking.ChunkByLines));

        // A generic method has no natural delegate type, so it is cast. A list of any item type is a list of values.
        table.Pure.Add(
            Module + "generate_covering_chunks",
            call => call.Signature("items", "k").InvokeAsync((Func<List<object?>, long, List<(List<object?>, List<long>)>>)ContentChunking.GenerateCoveringChunks));
    }
}
