using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;
using System.Reflection;

namespace ModelReader {

  public partial class ModelReader {

    public static SchemaRoot GetSchema(Assembly assembly, string modelNamespace) {
      SchemaRoot schemaRoot = new SchemaRoot();
      IEnumerable<Type> types = assembly.GetTypes().Where((Type t) => t.Namespace == modelNamespace);
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
      foreach (PropertyInfo propertyInfo in type.GetProperties()) {
        if (propertyInfo.Name == "RowVersion") { continue; }
        if (processedPropertyNames.Contains(propertyInfo.Name)) { continue; }
        bool isList = (
          !(typeof(string) == propertyInfo.PropertyType) &&
          typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType)
        );
        if (isList) {
          AddListNavigation(propertyInfo, entitySchema, schemaRoot);
        } else {
          bool isForeignKey = propertyInfo.Name.Length > 2 &&
            propertyInfo.Name.Substring(propertyInfo.Name.Length - 2, 2) == "Id";

          if (isForeignKey) {
            string navigationPropertyName = propertyInfo.Name.Substring(0, propertyInfo.Name.Length - 2);
            PropertyInfo navigationPropertyInfo = type.GetProperty(navigationPropertyName);
            if (navigationPropertyInfo == null) { continue; }
            processedPropertyNames.Add(navigationPropertyName);
            AddNavigation(propertyInfo, navigationPropertyInfo, entitySchema, schemaRoot);
          } else {
            string foreignKeyPropertyName = propertyInfo.Name + "Id";
            PropertyInfo foreignKeyProperty = type.GetProperty(foreignKeyPropertyName);
            if (foreignKeyProperty == null) {
              AddField(propertyInfo, entitySchema);
            } else {
              processedPropertyNames.Add(foreignKeyPropertyName);
              AddNavigation(foreignKeyProperty, propertyInfo, entitySchema, schemaRoot);
            }
          }
        }
      }
      schemaRoot.Entities = schemaRoot.Entities.Append(entitySchema).ToArray();
    }

    private static void AddField(PropertyInfo propertyInfo, EntitySchema entitySchema) {
      FieldSchema fieldSchema = new FieldSchema();
      fieldSchema.Name = propertyInfo.Name;
      fieldSchema.Type = propertyInfo.PropertyType.Name;
      bool required = propertyInfo.GetCustomAttribute<RequiredAttribute>() != null;
      fieldSchema.Required = required;
      entitySchema.Fields = entitySchema.Fields.Append(fieldSchema).ToArray();
    }

    private static void AddNavigation(
      PropertyInfo foreignKeyPropertyInfo, PropertyInfo navigationPropertyInfo,
      EntitySchema entitySchema, SchemaRoot schemaRoot
    ) {
      RelationSchema relationSchema = new RelationSchema();
      relationSchema.Name = navigationPropertyInfo.Name;
      relationSchema.PrimaryEntityName = entitySchema.Name;
      relationSchema.ForeignEntityName = navigationPropertyInfo.Name;
      relationSchema.ForeignKeyIndexName = foreignKeyPropertyInfo.Name;
      LookupAttribute lookupAttribute = navigationPropertyInfo.GetCustomAttribute<LookupAttribute>();
      relationSchema.IsLookupRelation = lookupAttribute != null;

      relationSchema.ForeignNavigationName = navigationPropertyInfo.Name;
      schemaRoot.Relations = schemaRoot.Relations.Append(relationSchema).ToArray();
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
      schemaRoot.Relations = schemaRoot.Relations.Append(relationSchema).ToArray();
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
