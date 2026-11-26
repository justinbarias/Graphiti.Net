// Ported from graphiti_core/utils/ontology_utils/entity_types_utils.py
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

namespace Graphiti.Core.Utils.OntologyUtils;

using Graphiti.Internal;

// PORT: a custom type is a GraphTypeDescriptor (R15, decision D26).
internal static class EntityTypesUtils
{
    // TODO(port): validate_entity_types
    internal static bool ValidateEntityTypes(OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        throw new NotSupportedException("TODO(port): validate_entity_types");
    }

    // TODO(port): validate_identity_properties
    internal static bool ValidateIdentityProperties(
        OrderedDictionary<string, List<string>>? identityProperties,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        throw new NotSupportedException("TODO(port): validate_identity_properties");
    }
}
