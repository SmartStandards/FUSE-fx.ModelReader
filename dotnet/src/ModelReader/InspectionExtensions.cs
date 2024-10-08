using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace System.Data.Fuse {

  internal static class InspectionExtensions {

    public static Type Obj2Null(this Type extendee) {
      if(extendee == null || extendee == typeof(Object)) {
        return null;
      }
      return extendee;
    }

    public static bool IsNullableType(this Type extendee) {
      return (extendee.IsGenericType && extendee.GetGenericTypeDefinition() == typeof(Nullable<>));
    }

    public static string GetTypeNameSave(this Type extendee, out bool isNullable) {

      isNullable = extendee.IsNullableType();

      if (isNullable) {
        bool dummy;
        return extendee.GetGenericArguments()[0].GetTypeNameSave(out dummy);
      }
      else {
        return extendee.Name;
      }

    }

    /// <summary>
    /// returns the correct Type also for byRef-/out-params where
    /// the type is usually encapsulated (leading to 'TypeName&' as string representation).
    /// </summary>
    public static Type ParameterTypeSafe(this ParameterInfo extendee) {
      if (extendee.ParameterType.IsByRef) {
        return extendee.ParameterType.GetElementType();
      }
      return extendee.ParameterType;
    }

    internal static Type GetUnwrappedType(this Type extendee) {
      if(extendee == null || extendee.FullName == "System.Void") {
        return null;
      }
      if (extendee.IsByRef) {
        extendee = extendee.GetElementType();
      }
      if (extendee.IsArray) {
        extendee = extendee.GetElementType();
      }
      else if (extendee.IsGenericType) {
        var genBase = extendee.GetGenericTypeDefinition();
        var genArg1 = extendee.GetGenericArguments()[0];
        if (typeof(List<>).MakeGenericType(genArg1).IsAssignableFrom(extendee)) {
          extendee = genArg1;
        }
        else if (typeof(Collection<>).MakeGenericType(genArg1).IsAssignableFrom(extendee)) {
          extendee = genArg1;
        }
        if (genBase == typeof(Nullable<>)) {
          extendee = genArg1;
        }
      }
      return extendee;
    }

  }

}