//DensityDetector.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;
using System;
using System.Linq;
using Trading.Core.Interfaces;

namespace Trading.Core.Trading;

public static class DensityDetector
{

    public static bool TryFind(
        OrderBookSnapshot snap,
        ScalperSettings settings,
        ILiquidityProvider liq,
        out DensitySignal signal)
    {
        signal = default;

        if (!liq.TryGet(snap.Symbol, DateTimeOffset.UtcNow, settings.LiquidityWindowMinutes, out var lq))
            return false;

        if (lq.DayVolumeShares < settings.MinDayVolumeShares)
            return false;



        var thresholdShares = lq.AvgWindowVolumeShares * settings.DensityCoef;

        var depth = Math.Max(1, settings.Depth);

        bool have = false;
        Side bestSide = Side.Buy;
        Level bestLvl = default;
        decimal bestShares = 0m;

        foreach (var b in snap.Bids.Take(depth))
        {
            var shares = settings.OrderBookSizeIsLots ? b.Size * lq.LotSize : b.Size;
            if (!have || shares > bestShares)
            {
                have = true;
                bestSide = Side.Buy;
                bestLvl = b;
                bestShares = shares;
            }
        }

        foreach (var a in snap.Asks.Take(depth))
        {
            var shares = settings.OrderBookSizeIsLots ? a.Size * lq.LotSize : a.Size;
            if (!have || shares > bestShares)
            {
                have = true;
                bestSide = Side.Sell;
                bestLvl = a;
                bestShares = shares;
            }
        }

        if (!have) return false;
        if (bestShares < thresholdShares) return false;

        signal = new DensitySignal(snap.Symbol, bestSide, bestLvl.Price, bestLvl.Size);
        return true;
    }
}