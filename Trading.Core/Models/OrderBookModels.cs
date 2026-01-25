//OrderBookModels.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Core.Models;

public record Level(decimal Price, decimal Size);

public record OrderBookSnapshot(
    string Symbol,
    DateTimeOffset Ts,
    IReadOnlyList<Level> Bids,
    IReadOnlyList<Level> Asks);
