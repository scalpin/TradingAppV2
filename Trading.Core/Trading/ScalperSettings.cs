//ScalperSettings.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Core.Trading;

// Настройки стратегии. Пока без UI, просто дефолты.
public sealed class ScalperSettings
{

    // Размер заявки (в акциях/лотах, как у тебя заведено)
    public decimal Qty { get; init; } = 1m;

    // qty в настройках — это лоты (true) или бумаги/shares (false)
    public bool OrderQtyIsLots { get; init; } = true;


    // --- новый критерий плотности ---
    public int LiquidityWindowMinutes { get; init; } = 5;

    // коэффициент чувствительности (пока 1)
    public decimal DensityCoef { get; init; } = 1m;

    // на сколько тиков сдвигать entryPrice
    public int EntryOffsetTicks { get; init; } = 1;

    // если стакан в лотах — умножаем на LotSize, иначе считаем что уже в акциях
    public bool OrderBookSizeIsLots { get; init; } = true;

    // чтобы отсеять совсем мусорные инструменты (можешь поставить 0 и выключить)
    public decimal MinDayVolumeShares { get; init; } = 100m;

    // тейк/развал/кулдаун
    public decimal TakeProfitPct { get; init; } = 0.001m;
    public decimal BreakFactor { get; init; } = 0.5m;
    public int CooldownMs { get; init; } = 2000;
    public int Depth { get; init; } = 20;
    public int BreakCheckMs { get; init; } = 200;
}
