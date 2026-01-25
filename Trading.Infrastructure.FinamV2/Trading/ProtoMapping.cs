//ProtoMapping.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;

namespace Trading.Infrastructure.FinamV2.Trading;

internal static class ProtoMapping
{
    public static Side MapSide(object? protoSide)
    {
        var s = protoSide?.ToString()?.ToUpperInvariant() ?? "";
        if (s.Contains("SELL")) return Side.Sell;
        return Side.Buy;
    }

    public static OrderStatus MapStatus(object? protoStatus)
    {
        var s = protoStatus?.ToString()?.ToUpperInvariant() ?? "";

        if (s.Contains("FILLED") || s.Contains("EXECUTED")) return OrderStatus.Filled;
        if (s.Contains("PART")) return OrderStatus.PartiallyFilled;
        if (s.Contains("CANCEL")) return OrderStatus.Canceled;
        if (s.Contains("REJECT")) return OrderStatus.Rejected;
        if (s.Contains("EXPIRE")) return OrderStatus.Expired;
        if (s.Contains("NEW") || s.Contains("ACCEPT")) return OrderStatus.New;

        return OrderStatus.Unknown;
    }
}