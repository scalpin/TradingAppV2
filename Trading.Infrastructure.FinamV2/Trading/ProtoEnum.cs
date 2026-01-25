//ProtoEnum.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Google.Protobuf.Reflection;

namespace Trading.Infrastructure.FinamV2.Trading;

internal static class ProtoEnum
{
    public static object ByOriginalName(Type enumType, string originalName)
    {
        if (!enumType.IsEnum)
            throw new ArgumentException($"{enumType.FullName} is not enum");

        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<OriginalNameAttribute>();
            if (attr?.Name == originalName)
                return field.GetValue(null)!;
        }

        throw new InvalidOperationException($"Enum {enumType.FullName} has no OriginalName '{originalName}'");
    }

    public static void SetByOriginalName<T>(T target, string propertyName, string originalName)
        where T : class
    {
        var prop = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found on {typeof(T).FullName}");

        var value = ByOriginalName(prop.PropertyType, originalName);
        prop.SetValue(target, value);
    }
}