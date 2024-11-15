using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;
using System.Reflection;

namespace System.Data.Fuse {

  public partial class ModelReader {

    [Obsolete("Please use the overload with Type[] as arg or 'GetSchemaForDbContext'")]
    public static SchemaRoot GetSchema(Assembly assembly, string modelNamespace) {
      SchemaRoot schemaRoot = new SchemaRoot();
      IEnumerable<Type> types = assembly.GetTypes().Where((Type t) => t.Namespace == modelNamespace);
      foreach (Type type in types) {
        AddModelType(schemaRoot, type, false);
      }
      return schemaRoot;
    }

    [Obsolete("Please use the overload with Type[] as arg or 'GetSchemaForDbContext'")]
    public static SchemaRoot GetSchema(Assembly assembly, string[] modelTypenames) {
      SchemaRoot schemaRoot = new SchemaRoot();
      IEnumerable<Type> types = assembly.GetTypes().Where(
        (Type t) => modelTypenames.Contains(t.Name) || modelTypenames.Contains(t.FullName)
      );
      foreach (Type type in types) {
        AddModelType(schemaRoot, type, false);
      }
      return schemaRoot;
    }

    public static SchemaRoot GetSchemaForDbContext<TDbContext>() {
      return GetSchemaForDbContext(typeof(TDbContext));
    }

    public static SchemaRoot GetSchemaForDbContext(Type dbContextType) {
      var props = dbContextType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
      var dbSetProps = props.Where((p) => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("DbSet"));
      Type[] principalEntityTypes = dbSetProps.Select((p) => p.PropertyType.GetGenericArguments()[0]).ToArray();
      return GetSchema(principalEntityTypes, true);
    }

    public static SchemaRoot GetSchema(Type[] modelTypes, bool recurseNavigationProps) {
      SchemaRoot schemaRoot = new SchemaRoot();
      foreach (Type type in modelTypes) {
        AddModelType(schemaRoot, type, recurseNavigationProps);
      }
      return schemaRoot;
    }

    private static void AddModelType(SchemaRoot schemaRoot, Type type, bool recurseNavigationProps) {

      if(schemaRoot.Entities.Where((e) => e.Name == type.Name).Any()) {
        return; //loop protection
      }

      EntitySchema entitySchema = new EntitySchema();
      entitySchema.Name = type.Name;
      entitySchema.NamePlural = type.Name + " Plural";
      List<string> processedPropertyNames = new List<string>();
      schemaRoot.Entities = schemaRoot.Entities.Union(new List<EntitySchema> { entitySchema }).ToArray();

      AddIndices(entitySchema, type);

      foreach (HasPrincipalAttribute principalAttribute in type.GetCustomAttributes<HasPrincipalAttribute>()) {
        AddPrincipalRelation(schemaRoot, type, principalAttribute);
      }

      foreach (HasDependentAttribute dependentAttribute in type.GetCustomAttributes<HasDependentAttribute>()) {
        AddDependentRelation(schemaRoot, type, dependentAttribute);
      }

      foreach (HasLookupAttribute lookupAttribute in type.GetCustomAttributes<HasLookupAttribute>()) {
        AddLookupRelation(schemaRoot, type, lookupAttribute);
      }

      Dictionary<PropertyInfo, Type> navigations = ModelRelationExtensions.GetNavigations(type, true, true, true, true);
      foreach (PropertyInfo propertyInfo in type.GetProperties()) {

        if (propertyInfo.Name == "RowVersion") { 
          continue;
        }

        if (processedPropertyNames.Contains(propertyInfo.Name)) {
          continue; 
        }

        if (
          schemaRoot.Relations.Any(
            (r) => (r.ForeignNavigationName == propertyInfo.Name && r.ForeignEntityName == entitySchema.Name) ||
             (r.PrimaryNavigationName == propertyInfo.Name && r.PrimaryEntityName == entitySchema.Name)
          )
        ) {
          continue;
        }

        if (navigations.Any((kvp) => kvp.Key.Name == propertyInfo.Name)) {
          Type navigationTarget = ResolveNavigationProperty(schemaRoot, propertyInfo);
          if (navigationTarget != null && recurseNavigationProps) {
            AddModelType(schemaRoot, navigationTarget, recurseNavigationProps);
          }
          continue;
        }

        AddField(propertyInfo, entitySchema);
      }

    }

    private static Type ResolveNavigationProperty(SchemaRoot schemaRoot, PropertyInfo propertyInfo) {
      bool isNavProp = false;
      Type navigationTarget = null;
      foreach (PrincipalAttribute attr in propertyInfo.GetCustomAttributes<PrincipalAttribute>()) {
        AddPrincipalRelationByNavigationProperty(schemaRoot, propertyInfo, attr);
        isNavProp = true;
      }
      foreach (LookupAttribute attr in propertyInfo.GetCustomAttributes<LookupAttribute>()) {
        AddLookupRelationByNavigationProperty(schemaRoot, propertyInfo, attr);
        isNavProp = true;
      }
      foreach (DependentAttribute attr in propertyInfo.GetCustomAttributes<DependentAttribute>()) {
        AddDependentRelationByNavigationProperty(schemaRoot, propertyInfo, attr);
        isNavProp = true;
      }
      foreach (ReferrerAttribute attr in propertyInfo.GetCustomAttributes<ReferrerAttribute>()) {
        AddReferrerRelationByNavigationProperty(schemaRoot, propertyInfo, attr);
        isNavProp = true;
      }
      if (isNavProp) {
        navigationTarget = propertyInfo.PropertyType.GetUnwrappedType();
      }
      return navigationTarget;
    }

    private static void AddPrincipalRelationByNavigationProperty(
      SchemaRoot schemaRoot, PropertyInfo propertyInfo, PrincipalAttribute principalAttribute
    ) {
      RelationSchema relationSchema = new RelationSchema();
      relationSchema.PrimaryEntityName = propertyInfo.PropertyType.Name;
      relationSchema.ForeignEntityName = propertyInfo.DeclaringType.Name;

      relationSchema.IsLookupRelation = false;

      relationSchema.ForeignNavigationName = propertyInfo.Name;
      string foreignKeyIndexName = propertyInfo.Name + "Id";
      if (propertyInfo.DeclaringType.GetProperties().Any((p) => p.Name == foreignKeyIndexName)) {
        relationSchema.ForeignKeyIndexName = foreignKeyIndexName;
      }
      AddOrUpdateRelationByForeignNavigation(schemaRoot, relationSchema);
    }

    private static void AddLookupRelationByNavigationProperty(
      SchemaRoot schemaRoot, PropertyInfo propertyInfo, LookupAttribute lookupAttribute
    ) {
      RelationSchema relationSchema = new RelationSchema();
      relationSchema.PrimaryEntityName = propertyInfo.PropertyType.Name;
      relationSchema.ForeignEntityName = propertyInfo.DeclaringType.Name;

      relationSchema.IsLookupRelation = true;

      relationSchema.ForeignNavigationName = propertyInfo.Name;
      string foreignKeyIndexName = propertyInfo.Name + "Id";
      if (propertyInfo.DeclaringType.GetProperties().Any((p) => p.Name == foreignKeyIndexName)) {
        relationSchema.ForeignKeyIndexName = foreignKeyIndexName;
      }
      AddOrUpdateRelationByForeignNavigation(schemaRoot, relationSchema);
    }

    private static void AddDependentRelationByNavigationProperty(
      SchemaRoot schemaRoot, PropertyInfo propertyInfo, DependentAttribute dependentAttribute
    ) {
      RelationSchema relationSchema = new RelationSchema();
      bool isMultiple = typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType);
      relationSchema.PrimaryEntityName = propertyInfo.DeclaringType.Name;
      relationSchema.ForeignEntityName = isMultiple ? propertyInfo.PropertyType.GetGenericArguments()[0].Name : propertyInfo.PropertyType.Name;

      relationSchema.IsLookupRelation = false;

      relationSchema.PrimaryNavigationName = propertyInfo.Name;
      relationSchema.ForeignEntityIsMultiple = isMultiple;
      AddOrUpdateRelationByPrimaryNavigation(schemaRoot, relationSchema);
    }

    private static void AddReferrerRelationByNavigationProperty(
      SchemaRoot schemaRoot, PropertyInfo propertyInfo, ReferrerAttribute lookupAttribute
    ) {
      RelationSchema relationSchema = new RelationSchema();
      bool isMultiple = typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType);
      relationSchema.PrimaryEntityName = propertyInfo.DeclaringType.Name;
      relationSchema.ForeignEntityName = isMultiple ? propertyInfo.PropertyType.GetGenericArguments()[0].Name : propertyInfo.PropertyType.Name;
      relationSchema.IsLookupRelation = true;

      relationSchema.PrimaryNavigationName = propertyInfo.Name;
      relationSchema.ForeignEntityIsMultiple = isMultiple;
      AddOrUpdateRelationByPrimaryNavigation(schemaRoot, relationSchema);
    }

    private static void AddLookupRelation(
      SchemaRoot schemaRoot, Type type, HasLookupAttribute lookupAttribute) {
      RelationSchema relationSchema = new RelationSchema();
      Type principalType = null;
      if (string.IsNullOrEmpty(lookupAttribute.LookupTypeName)) {
        PropertyInfo localNavigationProperty = type.GetProperty(lookupAttribute.LocalNavigationName);
        if (localNavigationProperty != null) {
          relationSchema.PrimaryEntityName = localNavigationProperty.PropertyType.Name;
          principalType = localNavigationProperty.PropertyType;
        }
      } else {
        principalType = type.Assembly.GetType(lookupAttribute.LookupTypeName);
        relationSchema.PrimaryEntityName = lookupAttribute.LookupTypeName;
      }

      relationSchema.PrimaryNavigationName = lookupAttribute.NavigationNameOnLookup;
      relationSchema.ForeignEntityName = type.Name;
      relationSchema.ForeignNavigationName = lookupAttribute.LocalNavigationName;
      relationSchema.ForeignKeyIndexName = lookupAttribute.LocalFkPropertyGroupName;
      relationSchema.IsLookupRelation = true;

      relationSchema.ForeignEntityIsMultiple = true;
      if (!string.IsNullOrEmpty(lookupAttribute.NavigationNameOnLookup) && principalType != null) {
        PropertyInfo navigationPropOnPrincipal = principalType.GetProperty(
           lookupAttribute.NavigationNameOnLookup
         );
        if (navigationPropOnPrincipal != null) {
          relationSchema.ForeignEntityIsMultiple = typeof(IEnumerable).IsAssignableFrom(
            navigationPropOnPrincipal.PropertyType
          );
        }
      }

      if (ContainsRelation(schemaRoot, relationSchema)) { return; }
      schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
    }

    private static void AddDependentRelation(SchemaRoot schemaRoot, Type type, HasDependentAttribute dependentAttribute) {
      RelationSchema relationSchema = new RelationSchema();
      relationSchema.PrimaryEntityName = type.Name;
      relationSchema.PrimaryNavigationName = dependentAttribute.LocalNavigationName;
      relationSchema.ForeignNavigationName = dependentAttribute.NavigationNameOnDependent;
      relationSchema.ForeignKeyIndexName = dependentAttribute.FkPropertyGroupNameOnDependent;
      relationSchema.IsLookupRelation = false;

      relationSchema.ForeignEntityIsMultiple = true;
      if (string.IsNullOrEmpty(dependentAttribute.DependentTypeName)) {
        if (!string.IsNullOrEmpty(dependentAttribute.LocalNavigationName)) {
          PropertyInfo localNavigationProp = type.GetProperty(dependentAttribute.LocalNavigationName);
          if (localNavigationProp != null) {
            bool isMultiple = typeof(IEnumerable).IsAssignableFrom(localNavigationProp.PropertyType);
            if (isMultiple) {
              relationSchema.ForeignEntityName = localNavigationProp.PropertyType.GetGenericArguments()[0].Name;

            } else {
              relationSchema.ForeignEntityName = localNavigationProp.PropertyType.Name;

              relationSchema.ForeignEntityIsMultiple = false;
            }
          }
        }
      } else {
        relationSchema.ForeignEntityName = dependentAttribute.DependentTypeName;

      }

      if (ContainsRelation(schemaRoot, relationSchema)) { return; }
      schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
    }

    private static void AddPrincipalRelation(SchemaRoot schemaRoot, Type type, HasPrincipalAttribute principalAttribute) {
      RelationSchema relationSchema = new RelationSchema();
      Type principalType = null;
      if (string.IsNullOrEmpty(principalAttribute.PrincipalTypeName)) {
        PropertyInfo localNavigationProperty = type.GetProperty(principalAttribute.LocalNavigationName);
        if (localNavigationProperty != null) {
          relationSchema.PrimaryEntityName = localNavigationProperty.PropertyType.Name;
          principalType = localNavigationProperty.PropertyType;
        }
      } else {
        principalType = type.Assembly.GetType(principalAttribute.PrincipalTypeName);
        relationSchema.PrimaryEntityName = principalAttribute.PrincipalTypeName;
      }

      relationSchema.PrimaryNavigationName = principalAttribute.NavigationNameOnPrincipal;
      relationSchema.ForeignEntityName = type.Name;
      relationSchema.ForeignNavigationName = principalAttribute.LocalNavigationName;
      relationSchema.ForeignKeyIndexName = principalAttribute.LocalFkPropertyGroupName;
      relationSchema.IsLookupRelation = false;

      relationSchema.ForeignEntityIsMultiple = true;
      if (!string.IsNullOrEmpty(principalAttribute.NavigationNameOnPrincipal) && principalType != null) {
        PropertyInfo navigationPropOnPrincipal = principalType.GetProperty(
           principalAttribute.NavigationNameOnPrincipal
         );
        if (navigationPropOnPrincipal != null) {
          relationSchema.ForeignEntityIsMultiple = typeof(IEnumerable).IsAssignableFrom(
            navigationPropOnPrincipal.PropertyType
          );
        }
      }

      if (ContainsRelation(schemaRoot, relationSchema)) { return; }
      schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
    }

    private static void AddOrUpdateRelationByPrimaryNavigation(SchemaRoot schemaRoot, RelationSchema relationSchema) {
      RelationSchema existingRelation = TryGetRelation(
        schemaRoot, relationSchema.PrimaryEntityName, relationSchema.ForeignEntityName
      );
      if (existingRelation != null) {
        existingRelation.PrimaryNavigationName = relationSchema.PrimaryNavigationName;
        existingRelation.ForeignEntityIsMultiple = relationSchema.ForeignEntityIsMultiple;
      } else {
        schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
      }
    }

    private static void AddOrUpdateRelationByForeignNavigation(SchemaRoot schemaRoot, RelationSchema relationSchema) {
      RelationSchema existingRelation = TryGetRelation(
        schemaRoot, relationSchema.PrimaryEntityName, relationSchema.ForeignEntityName
      );
      if (existingRelation != null) {
        existingRelation.ForeignNavigationName = relationSchema.ForeignNavigationName;
        existingRelation.ForeignKeyIndexName = relationSchema.ForeignKeyIndexName;
      } else {
        schemaRoot.Relations = schemaRoot.Relations.Union(new List<RelationSchema> { relationSchema }).ToArray();
      }
    }

    private static bool ContainsRelation(SchemaRoot schemaRoot, RelationSchema relationSchema) {
      return schemaRoot.Relations.Any(
        (r) => r.PrimaryNavigationName == relationSchema.PrimaryNavigationName &&
        r.PrimaryEntityName == relationSchema.PrimaryEntityName &&
        r.ForeignEntityName == relationSchema.ForeignEntityName &&
        r.ForeignKeyIndexName == relationSchema.ForeignKeyIndexName
      );
    }

    private static RelationSchema TryGetRelation(SchemaRoot schemaRoot, string primatyEntityName, string foreignEntityName) {
      return schemaRoot.Relations.FirstOrDefault(
        (r) =>
        r.PrimaryEntityName == primatyEntityName &&
        r.ForeignEntityName == foreignEntityName
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
      Type propertyType = propertyInfo.PropertyType;
      if (Nullable.GetUnderlyingType(propertyType) != null) {
        propertyType = propertyType.GetGenericArguments()[0];
      }
      fieldSchema.Type = propertyType.Name;
      bool required = propertyInfo.GetCustomAttribute<RequiredAttribute>() != null;
      fieldSchema.Required = required;

      Attribute setableAttribute = propertyInfo.GetCustomAttribute<SetableAttribute>();
      if (setableAttribute != null) {
        fieldSchema.SetabilityFlags = (int)((SetableAttribute)setableAttribute).Setability;
      }

      fieldSchema.IdentityLabel = propertyInfo.GetCustomAttribute<IdentityLabelAttribute>() != null;

      Attribute defaultValueAttribute = propertyInfo.GetCustomAttribute<DefaultValueAttribute>();
      if (defaultValueAttribute != null) {
        fieldSchema.DefaultValue = ((DefaultValueAttribute)defaultValueAttribute).Value.ToString();
      }

      entitySchema.Fields = entitySchema.Fields.Union(new List<FieldSchema> { fieldSchema }).ToArray();

    }

  }

}
