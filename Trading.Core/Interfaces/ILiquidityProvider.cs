//ILiquidityProvider.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Core.Interfaces;

public readonly record struct LiquiditySnapshot(
    decimal LotSize,
    decimal DayVolumeShares,
    decimal PriceStep,
    double ElapsedTradingMinutes,
    decimal AvgWindowVolumeShares);

public interface ILiquidityProvider
{
    bool TryGet(string symbol, DateTimeOffset nowUtc, int windowMinutes, out LiquiditySnapshot snap);
}