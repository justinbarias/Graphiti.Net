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
internal static class AttributeUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(AttributeUtils));

    internal const long DefaultAttributeMaxLength = 250;

    internal const long ListTotalLengthMultiplier = 8;

    internal const string EnvVar = "GRAPHITI_ATTRIBUTE_MAX_LENGTH";

    // PORT: a module-level "warned once" set is a static readonly field (R17). It is only tested with `in` (R14).
    internal static readonly HashSet<string> WarnedInvalidEnv = new(StringComparer.Ordinal);

    // PORT: the environment read runs at each call (R17).
    // TODO(port): _resolve_default_max_length
    internal static long ResolveDefaultMaxLength(long defaultMaxLength)
    {
        throw new NotSupportedException("TODO(port): _resolve_default_max_length");
    }

    // TODO(port): _field_max_length
    internal static long? FieldMaxLength(GraphTypeDescriptor model, string fieldName)
    {
        throw new NotSupportedException("TODO(port): _field_max_length");
    }

    // TODO(port): _field_is_required
    internal static bool FieldIsRequired(GraphTypeDescriptor model, string fieldName)
    {
        throw new NotSupportedException("TODO(port): _field_is_required");
    }

    // PORT: `value: Any` is a value of open data (R6).
    // TODO(port): _check_value_against_cap
    internal static (bool, string, long, long) CheckValueAgainstCap(object? value, long maxLen)
    {
        throw new NotSupportedException("TODO(port): _check_value_against_cap");
    }

    // PORT: the arguments after `model` are keyword-only in Python. `dropped` is iterated with no sort by
    // apply_capped_attributes, so it is a PySet<string> in insertion order (R14).
    // TODO(port): cap_string_attributes
    internal static (OrderedDictionary<string, object?>, PySet<string>) CapStringAttributes(
        OrderedDictionary<string, object?> response,
        GraphTypeDescriptor model,
        long defaultMaxLength = DefaultAttributeMaxLength,
        string promptName = "",
        string entityUuid = "",
        string groupId = "")
    {
        throw new NotSupportedException("TODO(port): cap_string_attributes");
    }

    // PORT: the arguments after `prior_attributes` are keyword-only in Python. `merge_mode: Literal['overlay',
    // 'replace']` is a string (R7).
    // TODO(port): apply_capped_attributes
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
        throw new NotSupportedException("TODO(port): apply_capped_attributes");
    }
}
