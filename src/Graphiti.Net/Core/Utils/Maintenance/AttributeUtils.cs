// Ported from graphiti_core/utils/maintenance/attribute_utils.py
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

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: `model: type[BaseModel]` is the custom type descriptor (R15): each caller passes a custom entity or edge type.
// `max_length` and `is_required` of a field are GraphFieldDescriptor.MaxLength and IsRequired (R15). The private
// names are internal, because judge cases read them (R2).
internal static partial class AttributeUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(AttributeUtils));

    // Default cap for free-form string attribute values. Calibrated to comfortably fit
    // normal short-text fields (phones, industries, URLs, addresses, alias lists) while
    // rejecting multi-paragraph LLM meta-reasoning. Customers with legitimately longer
    // fields should set an explicit max_length on the Pydantic Field, or override the
    // default globally via the GRAPHITI_ATTRIBUTE_MAX_LENGTH env var.
    internal const long DefaultAttributeMaxLength = 250;

    // When a list-typed string attribute is provided, the per-item cap and an aggregate
    // multiplier together bound list-total length. The multiplier mirrors common usage
    // (≤8 short entries) without being so loose that a 50× repetition slips through.
    internal const long ListTotalLengthMultiplier = 8;

    internal const string EnvVar = "GRAPHITI_ATTRIBUTE_MAX_LENGTH";

    // Track invalid env values we've already warned about so a misconfigured deploy
    // emits one warning per unique bad value rather than one per cap invocation.
    // PORT: a module-level "warned once" set is a static readonly field (R17). It is only tested with `in` (R14).
    internal static readonly HashSet<string> WarnedInvalidEnv = new(StringComparer.Ordinal);

    // Merge semantics shared by node and edge call sites:
    //
    //   merge_mode='overlay'  (node attributes)
    //       prior overlaid by kept fields. LLM-omitted fields retain prior values;
    //       cap-dropped fields also retain prior values (because they are absent
    //       from kept and overlay never overwrites with absence).
    //
    //   merge_mode='replace'  (edge attributes)
    //       kept fully replaces prior, BUT cap-dropped fields are restored from
    //       prior. LLM-omitted fields are cleared (replace semantics).
    //
    // The asymmetry exists because edge-attribute extraction historically used
    // wholesale replacement, while node attributes have always been incrementally
    // merged. The unified helper makes the choice explicit at each call site.

    // PORT: the environment read runs at each call (R17).
    internal static long ResolveDefaultMaxLength(long defaultMaxLength)
    {
        var raw = PyEnv.Str(EnvVar);
        if (raw is null || PyText.Strip(raw).Length == 0)
        {
            return defaultMaxLength;
        }

        long parsed;
        try
        {
            parsed = Py.Int(raw);
            if (parsed <= 0)
            {
                throw new ArgumentException("non-positive");
            }
        }
        catch (ArgumentException)
        {
            // PORT: `if raw not in s: s.add(raw)` is one Add call, which returns false for a value in the set (CA1868).
            if (WarnedInvalidEnv.Add(raw))
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    var rawRepr = Py.Repr(raw);
                    Log.IgnoringInvalidEnv(Logger, EnvVar, rawRepr, defaultMaxLength);
                }
            }

            return defaultMaxLength;
        }

        return parsed;
    }

    // PORT: `getattr(field_info, 'metadata', [])` and the `max_length` of its items are GraphFieldDescriptor.MaxLength
    // (R15). The descriptor holds the explicit max_length of the field, or null.
    internal static long? FieldMaxLength(GraphTypeDescriptor model, string fieldName)
    {
        if (!model.ModelFields.TryGetValue(fieldName, out var fieldInfo))
        {
            return null;
        }

        return fieldInfo.MaxLength;
    }

    // PORT: `is_required` is callable on each pydantic 2 field, so the descriptor holds its result (R15). The branch
    // for an older pydantic shape has no C# form.
    internal static bool FieldIsRequired(GraphTypeDescriptor model, string fieldName)
    {
        if (!model.ModelFields.TryGetValue(fieldName, out var fieldInfo))
        {
            return false;
        }

        return fieldInfo.IsRequired;
    }

    // PORT: `value: Any` is a value of open data (R6).

    /// <summary>
    /// Decide whether ``value`` exceeds the cap and on which axis.
    ///
    /// Returns ``(exceeded, reason, observed_length, breached_cap)``:
    ///   * ``exceeded``       — True if the field should be dropped.
    ///   * ``reason``         — one of ``'per_item'``, ``'aggregate'``, ``'ok'``.
    ///   * ``observed_length`` — the length to log: the offending element's length for
    ///     per-item triggers, the aggregate string length for aggregate triggers.
    ///   * ``breached_cap``    — the cap that was actually breached; ``max_len`` for
    ///     per-item, ``max_len * LIST_TOTAL_LENGTH_MULTIPLIER`` for aggregate. Logging
    ///     this alongside ``observed_length`` makes the breached cap clear.
    ///
    /// Catching both axes prevents a single bleed slipping through inside one element
    /// AND prevents many "just-under-cap" items adding up to KB-scale list bleed.
    /// </summary>
    internal static (bool, string, long, long) CheckValueAgainstCap(object? value, long maxLen)
    {
        if (value is string text)
        {
            if (Py.Len(text) > maxLen)
            {
                return (true, "per_item", Py.Len(text), maxLen);
            }

            return (false, "ok", 0, maxLen);
        }

        if (Py.TypeName(value) == "list")
        {
            var maxItem = Py.Iter(value).OfType<string>().Select(Py.Len).DefaultIfEmpty(0).Max();
            if (maxItem > maxLen)
            {
                return (true, "per_item", maxItem, maxLen);
            }

            var total = Py.Iter(value).OfType<string>().Sum(Py.Len);
            var aggregateCap = maxLen * ListTotalLengthMultiplier;
            if (total > aggregateCap)
            {
                return (true, "aggregate", total, aggregateCap);
            }

            return (false, "ok", 0, maxLen);
        }

        return (false, "ok", 0, maxLen);
    }

    // PORT: the arguments after `model` are keyword-only in Python. `dropped` is iterated with no sort by
    // apply_capped_attributes, so it is a PySet<string> in insertion order (R14).

    /// <summary>
    /// Drop string (or list-of-string) attributes whose value exceeds a length cap.
    ///
    /// Defends against meta-thinking / schema-description bleed where the LLM dumps
    /// multi-paragraph reasoning into a free-form attribute field.
    ///
    /// For string-typed fields the cap is the length of the value. For list-typed
    /// fields the cap is enforced both per-item and on the aggregate length of all
    /// string elements (max_len × ``LIST_TOTAL_LENGTH_MULTIPLIER``); see
    /// ``_check_value_against_cap``.
    ///
    /// Cap precedence: an explicit ``max_length`` on the Pydantic Field wins; otherwise
    /// the resolved default (``GRAPHITI_ATTRIBUTE_MAX_LENGTH`` env var if set, else
    /// ``default_max_length``). Non-string, non-string-list fields pass through untouched.
    ///
    /// Returns ``(kept, dropped)``:
    ///   * ``kept`` — the response dict with over-cap fields removed (with one exception,
    ///     below).
    ///   * ``dropped`` — the set of field names that were dropped.
    ///
    /// Required-field exception: if a Pydantic field is REQUIRED (no default and no
    /// ``Optional``) and the LLM emitted an over-cap value, the value is retained
    /// (with a warning) rather than dropped. Dropping a required field would cause
    /// the subsequent ``model(**capped)`` validation in the node path to fail the
    /// entire response. Customers who want stricter behavior on required fields
    /// should set an explicit ``max_length`` on the Pydantic Field; Pydantic will
    /// enforce it at validation time.
    ///
    /// Logging uses ``entity_uuid`` instead of the entity name to avoid exposing text values.
    /// </summary>
    internal static (OrderedDictionary<string, object?>, PySet<string>) CapStringAttributes(
        OrderedDictionary<string, object?> response,
        GraphTypeDescriptor model,
        long defaultMaxLength = DefaultAttributeMaxLength,
        string promptName = "",
        string entityUuid = "",
        string groupId = "")
    {
        var effectiveDefault = ResolveDefaultMaxLength(defaultMaxLength);
        var kept = new OrderedDictionary<string, object?>();
        var dropped = new PySet<string>(comparer: StringComparer.Ordinal);
        foreach (var (fieldName, value) in response)
        {
            // PORT: `x or default` is false for None and for 0 (Part 3).
            var maxLen = FieldMaxLength(model, fieldName) is long explicitMax && explicitMax != 0 ? explicitMax : effectiveDefault;
            var (exceeded, reason, observedLength, breachedCap) = CheckValueAgainstCap(value, maxLen);
            if (!exceeded)
            {
                kept[fieldName] = value;
                continue;
            }

            // Required-field carve-out: don't drop, would crash Pydantic validation.
            if (FieldIsRequired(model, fieldName))
            {
                Log.CapSkippedRequired(
                    Logger,
                    promptName,
                    string.IsNullOrEmpty(groupId) ? "<unknown>" : groupId,
                    string.IsNullOrEmpty(entityUuid) ? "<unknown>" : entityUuid,
                    fieldName,
                    reason,
                    observedLength,
                    breachedCap);
                kept[fieldName] = value;
                continue;
            }

            Log.CapExceeded(
                Logger,
                promptName,
                string.IsNullOrEmpty(groupId) ? "<unknown>" : groupId,
                string.IsNullOrEmpty(entityUuid) ? "<unknown>" : entityUuid,
                fieldName,
                reason,
                observedLength,
                breachedCap);
            dropped.Add(fieldName);
        }

        return (kept, dropped);
    }

    // PORT: the arguments after `prior_attributes` are keyword-only in Python. `merge_mode: Literal['overlay',
    // 'replace']` is a string (R7).

    /// <summary>
    /// Cap the LLM response and merge it with prior attributes per the merge mode.
    ///
    /// See the module-level docstring for the semantics of each ``merge_mode``. The
    /// return value is ``(merged, dropped)``; the dropped set is exposed for callers
    /// that want to log or react to it independently.
    /// </summary>
    internal static (OrderedDictionary<string, object?>, PySet<string>) ApplyCappedAttributes(
        OrderedDictionary<string, object?> response,
        GraphTypeDescriptor model,
        OrderedDictionary<string, object?> priorAttributes,
        string mergeMode,
        long defaultMaxLength = DefaultAttributeMaxLength,
        string promptName = "",
        string entityUuid = "",
        string groupId = "")
    {
        var (kept, dropped) = CapStringAttributes(
            response,
            model,
            defaultMaxLength: defaultMaxLength,
            promptName: promptName,
            entityUuid: entityUuid,
            groupId: groupId);

        // Structured-output clients may emit explicit nulls for fields where extraction
        // found no value. Keep known non-null prior values in that case, but retain the
        // null when there is no prior value so first extraction can materialize "unknown".
        var nullsKept = new OrderedDictionary<string, object?>();
        foreach (var (field, value) in kept)
        {
            nullsKept[field] = value is null && priorAttributes.TryGetValue(field, out var prior) && prior is not null
                ? prior
                : value;
        }

        kept = nullsKept;
        OrderedDictionary<string, object?> merged;
        if (mergeMode == "overlay")
        {
            merged = new OrderedDictionary<string, object?>(priorAttributes);
            foreach (var (field, value) in kept)
            {
                merged[field] = value;
            }
        }
        else if (mergeMode == "replace")
        {
            merged = new OrderedDictionary<string, object?>(kept);
            foreach (var field in dropped)
            {
                if (priorAttributes.TryGetValue(field, out var prior))
                {
                    merged[field] = prior;
                }
            }
        }
        else
        {
            // pragma: no cover — Literal protects this at type-check time.
            throw new ArgumentException($"merge_mode must be \"overlay\" or \"replace\", got {Py.Repr(mergeMode)}");
        }

        return (merged, dropped);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid {EnvVar}={Raw}; expected a positive integer. Using default={Default}.")]
        internal static partial void IgnoringInvalidEnv(ILogger logger, string envVar, string raw, long @default);

        [LoggerMessage(Level = LogLevel.Warning, Message = "attribute_length_cap_skipped_required prompt={Prompt} group_id={GroupId} entity_uuid={EntityUuid} field={Field} reason={Reason} length={Length} cap={Cap}")]
        internal static partial void CapSkippedRequired(ILogger logger, string prompt, string groupId, string entityUuid, string field, string reason, long length, long cap);

        [LoggerMessage(Level = LogLevel.Information, Message = "attribute_length_cap_exceeded prompt={Prompt} group_id={GroupId} entity_uuid={EntityUuid} field={Field} reason={Reason} length={Length} cap={Cap}")]
        internal static partial void CapExceeded(ILogger logger, string prompt, string groupId, string entityUuid, string field, string reason, long length, long cap);
    }
}
