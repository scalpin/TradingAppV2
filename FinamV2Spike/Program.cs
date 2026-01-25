//FinamV2Spike/Program.cs
using Trading.Infrastructure.FinamV2;
using Trading.Core.Models;

var secret = Environment.GetEnvironmentVariable("FINAM_SECRET");
if (string.IsNullOrWhiteSpace(secret))
{
    Console.WriteLine("FINAM_SECRET not set");
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var host = new FinamV2Host(secret);

host.OrderBookFeed.OrderBook += snap =>
{
    // не спамь, печатай раз в секунду или по таймеру
    var bid = snap.Bids.FirstOrDefault();
    var ask = snap.Asks.FirstOrDefault();
    if (bid != null && ask != null)
        Console.WriteLine($"{snap.Symbol} bid={bid.Price} ask={ask.Price}");
};

host.Trading.OrderUpdated += o =>
    Console.WriteLine($"order {o.OrderId} {o.Symbol} {o.Side} {o.Status}");

host.Trading.Trade += t =>
    Console.WriteLine($"trade {t.TradeId} {t.Symbol} {t.Side} {t.Price} x {t.Qty}");

var symbols = new[] { "SBER@MISX" };

var started = await host.StartAsync(symbols, Console.WriteLine, cts.Token);
Console.WriteLine($"accountId={started.accountId}");

await Task.Delay(Timeout.Infinite, cts.Token);