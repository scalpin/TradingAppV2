using System.Collections.Generic;
using System.Globalization;
using Grpc.Tradeapi.V1.Marketdata;
using Trading.Infrastructure.FinamV2.FinamGrpc;


namespace Trading.Infrastructure.FinamV2.MarketData;

public sealed class OrderBookCache
{
    private readonly object _gate = new();

    private readonly SortedDictionary<decimal, decimal> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));
    private readonly SortedDictionary<decimal, decimal> _asks = new();

    public void ApplyRow(StreamOrderBook.Types.Row row)
    {
        var price = ProtoDecimal.Parse(row.Price?.Value);
        if (price is null) return;

        bool isBid;
        decimal? size;

        if (row.BuySize != null)
        {
            isBid = true;
            size = ProtoDecimal.Parse(row.BuySize.Value);
        }
        else if (row.SellSize != null)
        {
            isBid = false;
            size = ProtoDecimal.Parse(row.SellSize.Value);
        }
        else return;

        if (size is null) return;

        lock (_gate)
        {
            var book = isBid ? _bids : _asks;

            // не гадаем про enum имена, работаем через ToString()
            var a = row.Action.ToString().ToUpperInvariant();
            var isRemove = a.Contains("REMOVE") || a.Contains("DELETE");

            if (isRemove)
            {
                book.Remove(price.Value);
                return;
            }

            if (size.Value <= 0) book.Remove(price.Value);
            else book[price.Value] = size.Value;
        }
    }

    public (decimal? bid, decimal? ask) Best()
    {
        lock (_gate)
        {
            decimal? bid = null;
            decimal? ask = null;

            foreach (var kv in _bids) { bid = kv.Key; break; }
            foreach (var kv in _asks) { ask = kv.Key; break; }

            return (bid, ask);
        }
    }

    public (List<(decimal p, decimal s)> bids, List<(decimal p, decimal s)> asks) Top(int n)
    {
        lock (_gate)
        {
            var b = new List<(decimal, decimal)>(n);
            var a = new List<(decimal, decimal)>(n);

            foreach (var kv in _bids)
            {
                b.Add((kv.Key, kv.Value));
                if (b.Count >= n) break;
            }

            foreach (var kv in _asks)
            {
                a.Add((kv.Key, kv.Value));
                if (a.Count >= n) break;
            }

            return (b, a);
        }
    }
}
