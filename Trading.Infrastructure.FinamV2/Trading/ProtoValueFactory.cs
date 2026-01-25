//ProtoValueFactory.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Reflection;


namespace Trading.Infrastructure.FinamV2.Trading;

internal static class ProtoValueFactory
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static object? CreateProtoDecimal(Type protoDecimalType, decimal value)
    {
        var obj = Activator.CreateInstance(protoDecimalType);
        if (obj == null) return null;

        var valueProp = protoDecimalType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueProp == null) return null;

        valueProp.SetValue(obj, value.ToString(Inv));
        return obj;
    }

    public static decimal? ReadProtoDecimal(object? protoDecimal)
    {
        if (protoDecimal == null) return null;

        var t = protoDecimal.GetType();
        var valueProp = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        var s = valueProp?.GetValue(protoDecimal) as string;

        if (string.IsNullOrWhiteSpace(s)) return null;
        if (decimal.TryParse(s, NumberStyles.Any, Inv, out var v)) return v;

        return null;
    }
}
