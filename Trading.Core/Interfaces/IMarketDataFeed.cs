//IMarketDataFeed.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;

namespace Trading.Core.Interfaces;

public interface IMarketDataFeed
{
    event Action<OrderBookSnapshot> OrderBook;
    Task StartAsync(IEnumerable<string> symbols, CancellationToken ct);
}
