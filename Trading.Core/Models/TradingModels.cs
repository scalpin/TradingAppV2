//TradingModels.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Core.Models;

public enum Side { Buy, Sell }

public enum OrderStatus
{
    Unknown,
    New,
    PartiallyFilled,
    Filled,
    Canceled,
    Rejected,
    Expired
}

public record OrderUpdate(
    string OrderId,
    string Symbol,
    Side Side,
    OrderStatus Status,
    decimal? Price,
    decimal? Qty,
    string? ClientOrderId);

public record TradeUpdate(
    string TradeId,
    string Symbol,
    Side Side,
    decimal Price,
    decimal Qty,
    DateTimeOffset Ts);
