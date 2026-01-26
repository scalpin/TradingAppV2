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

    // Минимальная плотность на уровне (объём), чтобы считать это сигналом
    public decimal DensityMinSize { get; init; } = 3000m;

    // Тейк-профит в процентах от цены входа (0.001 = 0.1%)
    public decimal TakeProfitPct { get; init; } = 0.001m;

    // Плотность считается "разваленной", если стала меньше исходной * BreakFactor
    public decimal BreakFactor { get; init; } = 0.5m;

    // Как часто можно входить повторно по одному символу
    public int CooldownMs { get; init; } = 2000;

    // Сколько уровней стакана анализировать
    public int Depth { get; init; } = 20;

    // Период проверки развала плотности во время удержания позиции
    public int BreakCheckMs { get; init; } = 200;
}
