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
    internal static bool ValidateEntityTypes(OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        if (entityTypes is null)
        {
            return true;
        }

        var entityNodeFieldNames = EntityNode.ModelFields.Keys;

        foreach (var (entityTypeName, entityTypeModel) in entityTypes)
        {
            var entityTypeFieldNames = entityTypeModel.ModelFields.Keys;
            foreach (var entityTypeFieldName in entityTypeFieldNames)
            {
                if (entityNodeFieldNames.Contains(entityTypeFieldName))
                {
                    throw new EntityTypeValidationException(entityTypeName, entityTypeFieldName);
                }
            }
        }

        return true;
    }

    internal static bool ValidateIdentityProperties(
        OrderedDictionary<string, List<string>>? identityProperties,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        if (identityProperties is null)
        {
            return true;
        }

        foreach (var (entityTypeName, propertyNames) in identityProperties)
        {
            if (entityTypes is null || !entityTypes.TryGetValue(entityTypeName, out var entityType))
            {
                throw new IdentityPropertyValidationException(
                    entityTypeName,
                    "entity type is not defined in entity_types");
            }

            if (propertyNames is null || propertyNames.Count == 0)
            {
                throw new IdentityPropertyValidationException(
                    entityTypeName,
                    "property list must be a non-empty list");
            }

            var entityTypeFields = entityType.ModelFields;
            foreach (var propertyName in propertyNames)
            {
                if (!entityTypeFields.ContainsKey(propertyName))
                {
                    throw new IdentityPropertyValidationException(
                        entityTypeName,
                        $"{propertyName} is not a field of the entity type");
                }
            }
        }

        return true;
    }
}
