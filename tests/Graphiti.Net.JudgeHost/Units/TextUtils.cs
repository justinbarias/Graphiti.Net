namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/text_utils.py</c>.</summary>
internal static class TextUtilsBindings
{
    private const string Module = "graphiti_core.utils.text_utils:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "truncate_utf8_to_bytes", call => call.Signature("text", "max_bytes").InvokeAsync(TextUtils.TruncateUtf8ToBytes));
        table.Pure.Add(Module + "truncate_at_sentence", call => call.Signature("text", "max_chars").InvokeAsync(TextUtils.TruncateAtSentence));
        table.Pure.Add(Module + "concatenate_episodes", call => call.Signature("episodes").InvokeAsync(TextUtils.ConcatenateEpisodes));
        table.Pure.Add(Module + "concatenate_timeline", call => call.Signature("episodes", "timeline").InvokeAsync(TextUtils.ConcatenateTimeline));
    }
}
