using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;
using System.Reflection;

namespace System.Data.Fuse {

  public partial class ModelReader {

    public static SchemaRoot GetSchema(Assembly assembly, string modelNamespace) {
      SchemaRoot schemaRoot = new SchemaRoot();
      IEnumerable<Type> types = assembly.GetTypes().Where((Type t) => t.Namespace == modelNamespace);
      foreach (Type type in types) {
        AddModelType(schemaRoot, type);
      }
      return schemaRoot;
    }

    public static SchemaRoot GetSchema(Assembly assembly, string[] modelTypenames) {
      SchemaRoot schemaRoot = new SchemaRoot();
      IEnumerable<Type> types = assembly.GetTypes().Where((Type t) => modelTypenames.Contains(t.Name));
      foreach (Type type in types) {
        AddModelType(schemaRoot, type);
      }
      return schemaRoot;
    }

    private static void AddModelType(SchemaRoot schemaRoot, Type type) {
      EntitySchema entitySchema = new EntitySchema();
      entitySchema.Name = type.Name.ToClearName();
      entitySchema.NamePlural = type.Name.ToClearName() + " Plural";
      List<string> processedPropertyNames = new List<string>();

      AddIndices(entitySchema, type);

      Dictionary<PropertyInfo, Type> navigations = ModelRelationExtensions.GetNavigations(type, true, true, true, true);
      foreach (PropertyInfo propertyInfo in type.GetProperties()) {
        if (propertyInfo.Name == "RowVersion") { continue; }
        if (processedPropertyNames.Contains(propertyInfo.Name)) { continue; }
        if (navigations.Any((kvp) => kvp.Key.Name == propertyInfo.Name)) { continue; }

        AddField(propertyInfo, entitySchema);

        foreach (HasPrincipalAttribute principalAttribute in type.GetCustomAttributes<HasPrincipalAttribute>()) {
          RelationSchema relationSchema = new RelationSchema();
          PropertyInfo localNavigationProperty = type.GetProperty(principalAttribute.LocalNavigationName);
          if (localNavigationProperty == null) { continue; }
          relationSchema.PrimaryEntityName = localNavigationProperty.PropertyType.Name;
          relationSchema.PrimaryNavigationName = principalAttribute.NavigationNameOnPrincipal;
          relationSchema.ForeignEntityName = type.Name;
          relationSchema.ForeignNavigationName = principalAttribute.LocalNavigationName;
          relationSchema.ForeignKeyIndexName = principalAttribute.LocalFkPropertyGroupName;

          PropertyInfo navigationPropOnPrincipal = localNavigationProperty.PropertyType.GetProperty(
            principalAttribute.NavigationNameOnPrincipal
          );
          if (navigationPropOnPrincipal != null) {
            relationSchema.ForeignEntityIsMultiple = typeof(IEnumerable).IsAssignableFrom(
              navigationPropOnPrincipal.PropertyType
            );
          }

          if (ContainsRelation(schemaRoot, relationSchema)) { continue; }
          schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
        }

        foreach (HasDependentAttribute dependentAttribute in type.GetCustomAttributes<HasDependentAttribute>()) {
          RelationSchema relationSchema = new RelationSchema();
          relationSchema.PrimaryEntityName = type.Name;
          relationSchema.PrimaryNavigationName = dependentAttribute.LocalNavigationName;

          PropertyInfo localNavigationProp = type.GetProperty(dependentAttribute.LocalNavigationName);
          if (localNavigationProp == null) { continue; }
          bool isMultiple = typeof(IEnumerable).IsAssignableFrom(localNavigationProp.PropertyType);
          if (isMultiple) {
            relationSchema.ForeignEntityIsMultiple = true;
            Type elementType = localNavigationProp.PropertyType.GetGenericArguments()[0];
            relationSchema.ForeignEntityName = elementType.Name;
          } else {
            relationSchema.ForeignEntityName = localNavigationProp.PropertyType.Name;
          }
          relationSchema.ForeignNavigationName = dependentAttribute.NavigationNameOnDependent;
          relationSchema.ForeignKeyIndexName = dependentAttribute.FkPropertyGroupNameOnDependent;                   

          if (ContainsRelation(schemaRoot, relationSchema)) { continue; }
          schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
        }

      }
      schemaRoot.Entities = schemaRoot.Entities.Union(new List<EntitySchema> { entitySchema }).ToArray();
    }

    private static bool ContainsRelation(SchemaRoot schemaRoot, RelationSchema relationSchema) {
      return schemaRoot.Relations.Any(
        (r) => r.PrimaryNavigationName == relationSchema.PrimaryNavigationName &&
        r.PrimaryEntityName == relationSchema.PrimaryEntityName &&
        r.ForeignEntityName == relationSchema.ForeignEntityName &&
        r.ForeignKeyIndexName == relationSchema.ForeignKeyIndexName &&
        r.ForeignEntityName == relationSchema.ForeignEntityName
      );
    }

    private static void AddIndices(EntitySchema entitySchema, Type type) {
      IEnumerable<PropertyGroupAttribute> propertyGroups = type.GetCustomAttributes<PropertyGroupAttribute>();
      IEnumerable<UniquePropertyGroupAttribute> uniquePropertyGroups = type.GetCustomAttributes<UniquePropertyGroupAttribute>();
      PrimaryIdentityAttribute primaryIdentity = type.GetCustomAttribute<PrimaryIdentityAttribute>();
      List<IndexSchema> indexes = new List<IndexSchema>();
      foreach (PropertyGroupAttribute propertyGroup in propertyGroups) {
        if (entitySchema.Indices.Any((i) => i.Name == propertyGroup.GroupName)) continue;
        indexes.Add(
          new IndexSchema() {
            Name = propertyGroup.GroupName,
            MemberFieldNames = propertyGroup.PropertyNames,
            Unique = false
          }
        );
      }
      foreach (UniquePropertyGroupAttribute propertyGroup in uniquePropertyGroups) {
        if (entitySchema.Indices.Any((i) => i.Name == propertyGroup.GroupName)) continue;
        indexes.Add(
          new IndexSchema() {
            Name = propertyGroup.GroupName,
            MemberFieldNames = propertyGroup.PropertyNames,
            Unique = true
          }
        );
      }
      entitySchema.Indices = indexes.ToArray();
      if (primaryIdentity != null) {
        entitySchema.PrimaryKeyIndexName = primaryIdentity.PropertyGroupName;
      }
    }

    private static void AddField(PropertyInfo propertyInfo, EntitySchema entitySchema) {
      FieldSchema fieldSchema = new FieldSchema();
      fieldSchema.Name = propertyInfo.Name;
      fieldSchema.Type = propertyInfo.PropertyType.Name;
      bool required = propertyInfo.GetCustomAttribute<RequiredAttribute>() != null;
      fieldSchema.Required = required;
      entitySchema.Fields = entitySchema.Fields.Union(new List<FieldSchema> { fieldSchema }).ToArray();

    }

    private static void AddNavigation(
      PropertyInfo foreignKeyPropertyInfo, PropertyInfo navigationPropertyInfo,
      EntitySchema entitySchema, SchemaRoot schemaRoot
    ) {
      RelationSchema relationSchema = new RelationSchema();
      relationSchema.Name = navigationPropertyInfo.Name;
      relationSchema.PrimaryEntityName = entitySchema.Name;
      relationSchema.ForeignEntityName = navigationPropertyInfo.PropertyType.Name;
      relationSchema.ForeignKeyIndexName = foreignKeyPropertyInfo.Name;
      LookupAttribute lookupAttribute = navigationPropertyInfo.GetCustomAttribute<LookupAttribute>();
      relationSchema.IsLookupRelation = lookupAttribute != null;

      relationSchema.ForeignNavigationName = navigationPropertyInfo.Name;
      schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();

    }

    private static void AddListNavigation(
      PropertyInfo navigationPropertyInfo, EntitySchema entitySchema, SchemaRoot schemaRoot
    ) {
      Type foreignEntityType = navigationPropertyInfo.PropertyType.GetGenericArguments()[0];

      RelationSchema relationSchema = new RelationSchema();
      relationSchema.Name = navigationPropertyInfo.Name;
      relationSchema.PrimaryEntityName = entitySchema.Name;
      relationSchema.ForeignEntityName = foreignEntityType.Name.ToClearName();
      LookupAttribute lookupAttribute = navigationPropertyInfo.GetCustomAttribute<LookupAttribute>();
      relationSchema.IsLookupRelation = lookupAttribute != null;
      relationSchema.ForeignEntityIsMultiple = true;
      relationSchema.ForeignNavigationName = navigationPropertyInfo.Name;
      relationSchema.PrimaryNavigationName = navigationPropertyInfo.Name;
      schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
    }
  }

  internal static class StringExtensions {
    public static string ToClearName(this string value) {
      if (!value.EndsWith("Entity")) {
        return value;
      }
      return value.Substring(0, value.Length - 6);
    }
  }

}
