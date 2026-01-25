//ProtoDecimal.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Infrastructure.FinamV2.FinamGrpc;

public static class ProtoDecimal
{
    public static decimal? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    public static string Format(decimal v) =>
        v.ToString(CultureInfo.InvariantCulture);
}