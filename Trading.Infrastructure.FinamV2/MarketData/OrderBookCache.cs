//OrderBookCache.cs
using System.Collections.Generic;
using System.Threading;
using Grpc.Tradeapi.V1.Marketdata;
using Trading.Core.Models;
using Trading.Infrastructure.FinamV2.FinamGrpc;

namespace Trading.Infrastructure.FinamV2.MarketData;

public sealed class OrderBookCache
{
    private readonly object _gate = new();

    private readonly SortedDictionary<decimal, decimal> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));
    private readonly SortedDictionary<decimal, decimal> _asks = new();

    private int _dirty; // 0/1

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

        var a = row.Action.ToString().ToUpperInvariant();
        var isRemove = a.Contains("REMOVE") || a.Contains("DELETE");

        var changed = false;

        lock (_gate)
        {
            var book = isBid ? _bids : _asks;

            if (isRemove)
            {
                changed = book.Remove(price.Value);
            }
            else
            {
                if (size.Value <= 0)
                {
                    changed = book.Remove(price.Value);
                }
                else
                {
                    if (book.TryGetValue(price.Value, out var old))
                        changed = old != size.Value;
                    else
                        changed = true;

                    book[price.Value] = size.Value;
                }
            }
        }

        if (changed)
            Volatile.Write(ref _dirty, 1);
    }

    public bool TryBuildSnapshot(int depth, out Level[] bids, out Level[] asks)
    {
        bids = System.Array.Empty<Level>();
        asks = System.Array.Empty<Level>();

        if (Interlocked.Exchange(ref _dirty, 0) == 0)
            return false;

        lock (_gate)
        {
            var blen = System.Math.Min(depth, _bids.Count);
            var alen = System.Math.Min(depth, _asks.Count);

            bids = new Level[blen];
            asks = new Level[alen];

            var i = 0;
            foreach (var kv in _bids)
            {
                if (i >= blen) break;
                bids[i++] = new Level(kv.Key, kv.Value);
            }

            i = 0;
            foreach (var kv in _asks)
            {
                if (i >= alen) break;
                asks[i++] = new Level(kv.Key, kv.Value);
            }

            return true;
        }
    }
}
