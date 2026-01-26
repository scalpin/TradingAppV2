//DensityDetector.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;

namespace Trading.Core.Trading;

public static class DensityDetector
{
    public static bool TryFind(OrderBookSnapshot snap, ScalperSettings s, out DensitySignal signal)
    {
        // Ищем кандидата в bids (плотность на покупку)
        var bid = snap.Bids.Take(s.Depth).FirstOrDefault(l => l.Size >= s.DensityMinSize);

        // Ищем кандидата в asks (плотность на продажу)
        var ask = snap.Asks.Take(s.Depth).FirstOrDefault(l => l.Size >= s.DensityMinSize);

        // Ничего нет
        if (bid is null && ask is null)
        {
            signal = default;
            return false;
        }

        // Если есть оба — берём тот, у которого объём больше (грубый, но рабочий критерий)
        if (bid is not null && ask is not null)
        {
            signal = bid.Size >= ask.Size
                ? new DensitySignal(snap.Symbol, Side.Buy, bid.Price, bid.Size)
                : new DensitySignal(snap.Symbol, Side.Sell, ask.Price, ask.Size);
            return true;
        }

        // Есть только один
        if (bid is not null)
        {
            signal = new DensitySignal(snap.Symbol, Side.Buy, bid.Price, bid.Size);
            return true;
        }

        signal = new DensitySignal(snap.Symbol, Side.Sell, ask!.Price, ask.Size);
        return true;
    }
}
