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

internal static class EntityTypesUtils
{
    // TODO(port): validate_entity_types is not declared. Its parameter `entity_types: dict[str, type[BaseModel]] | None`
    // is OrderedDictionary<string, GraphTypeDescriptor>? (R15, decision D26), and the lead has not written
    // Graphiti.Internal.GraphTypeDescriptor yet. The signature is:
    // internal static bool ValidateEntityTypes(OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)

    // TODO(port): validate_identity_properties is not declared, for the same reason. The signature is:
    // internal static bool ValidateIdentityProperties(OrderedDictionary<string, List<string>>? identityProperties, OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
}
