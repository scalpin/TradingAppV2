//ITradingGateway.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;

namespace Trading.Core.Interfaces;

public interface ITradingGateway
{
    event Action<OrderUpdate> OrderUpdated;
    event Action<TradeUpdate> Trade;

    Task<string> PlaceLimitAsync(string accountId, string symbol, Side side, decimal price, decimal qty, string? clientOrderId, CancellationToken ct);
    Task<string> PlaceMarketAsync(string accountId, string symbol, Side side, decimal qty, string? clientOrderId, CancellationToken ct);
    Task CancelAsync(string accountId, string orderId, CancellationToken ct);
}
