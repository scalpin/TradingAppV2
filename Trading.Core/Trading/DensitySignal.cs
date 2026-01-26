//DensitySignal.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Core.Models;

namespace Trading.Core.Trading;

// Сигнал: плотность найдена на конкретной цене и стороне
public readonly record struct DensitySignal(
    string Symbol,
    Side Side,        // Buy => плотность в bids, Sell => плотность в asks
    decimal Price,
    decimal Size);