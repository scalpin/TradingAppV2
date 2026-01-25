using System.Collections.Generic;
using System.Globalization;
using Grpc.Tradeapi.V1.Marketdata;

public sealed class OrderBookCache
{
    private readonly object _gate = new();

    private readonly SortedDictionary<decimal, decimal> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a))); // desc
    private readonly SortedDictionary<decimal, decimal> _asks = new(); // asc

    public void ApplyRow(OrderBook.Types.Row row)
    {
        var price = ParseDecimal(row.Price?.Value);
        if (price is null) return;

        // oneof: либо buySize, либо sellSize
        bool isBid;
        decimal? size;

        if (row.BuySize != null)
        {
            isBid = true;
            size = ParseDecimal(row.BuySize.Value);
        }
        else if (row.SellSize != null)
        {
            isBid = false;
            size = ParseDecimal(row.SellSize.Value);
        }
        else
        {
            return;
        }

        if (size is null) return;

        var action = NormalizeAction(row.Action);

        lock (_gate)
        {
            var book = isBid ? _bids : _asks;

            if (action == "REMOVE")
            {
                book.Remove(price.Value);
                return;
            }

            // ADD или UPDATE
            if (size.Value <= 0)
                book.Remove(price.Value);
            else
                book[price.Value] = size.Value;
        }
    }

    public (decimal? price, decimal? size) BestBid()
    {
        lock (_gate)
        {
            foreach (var kv in _bids) return (kv.Key, kv.Value);
            return (null, null);
        }
    }

    public (decimal? price, decimal? size) BestAsk()
    {
        lock (_gate)
        {
            foreach (var kv in _asks) return (kv.Key, kv.Value);
            return (null, null);
        }
    }

    public IEnumerable<(decimal price, decimal size)> TopBids(int n)
    {
        lock (_gate)
        {
            var i = 0;
            foreach (var kv in _bids)
            {
                yield return (kv.Key, kv.Value);
                if (++i >= n) yield break;
            }
        }
    }

    public IEnumerable<(decimal price, decimal size)> TopAsks(int n)
    {
        lock (_gate)
        {
            var i = 0;
            foreach (var kv in _asks)
            {
                yield return (kv.Key, kv.Value);
                if (++i >= n) yield break;
            }
        }
    }

    private static decimal? ParseDecimal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    private static string NormalizeAction(OrderBook.Types.Row.Types.Action action)
    {
        // чтобы не зависеть от того, как именно enum сгенерился
        var name = action.ToString().ToUpperInvariant();

        if (name.Contains("REMOVE") || name.Contains("DELETE"))
            return "REMOVE";

        if (name.Contains("UPDATE"))
            return "UPDATE";

        if (name.Contains("ADD") || name.Contains("INSERT"))
            return "ADD";

        // если там внезапно "ACTION_UNSPECIFIED" или что-то похожее
        return name;
    }
}
