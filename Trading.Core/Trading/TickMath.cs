using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Trading.Core.Models;

namespace Trading.Core.Trading;

public static class TickMath
{
    public static decimal? DeriveStepFromOrderBook(OrderBookSnapshot snap, int depth = 20)
    {
        var prices = snap.Bids.Take(depth).Select(x => x.Price)
            .Concat(snap.Asks.Take(depth).Select(x => x.Price))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (prices.Length < 2) return null;

        decimal? min = null;
        for (int i = 1; i < prices.Length; i++)
        {
            var d = prices[i] - prices[i - 1];
            if (d <= 0) continue;
            if (min is null || d < min) min = d;
        }
        return min;
    }

    public static decimal RoundToStep(decimal price, decimal step)
    {
        if (step <= 0) return price;
        var n = decimal.Round(price / step, 0, MidpointRounding.AwayFromZero);
        return n * step;
    }

    public static decimal RoundUpToStep(decimal price, decimal step)
    {
        if (step <= 0) return price;
        var n = decimal.Ceiling(price / step);
        return n * step;
    }

    public static decimal RoundDownToStep(decimal price, decimal step)
    {
        if (step <= 0) return price;
        var n = decimal.Floor(price / step);
        return n * step;
    }

    // "перед плотностью":
    // Buy-плотность: ставим BUY выше (density + step*ticks)
    // Sell-плотность: ставим SELL ниже (density - step*ticks)
    public static decimal ShiftFromDensity(Side side, decimal densityPrice, decimal step, int ticks)
    {
        if (step <= 0 || ticks == 0) return densityPrice;
        return side == Side.Buy
            ? densityPrice + step * ticks
            : densityPrice - step * ticks;
    }

    // не даём лимитке мгновенно пересечь спред
    public static decimal ClampNonCrossing(Side side, decimal price, OrderBookSnapshot snap, decimal step)
    {
        if (step <= 0) return price;

        var bestBid = snap.Bids.FirstOrDefault()?.Price;
        var bestAsk = snap.Asks.FirstOrDefault()?.Price;
        if (bestBid is null || bestAsk is null) return price;

        if (side == Side.Buy)
        {
            var maxBuy = bestAsk.Value - step;     // buy < ask
            if (price > maxBuy) price = maxBuy;
        }
        else
        {
            var minSell = bestBid.Value + step;    // sell > bid
            if (price < minSell) price = minSell;
        }

        return price;
    }
}