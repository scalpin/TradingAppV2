//TradingRuntime.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Infrastructure.FinamV2.Trading;
using Trading.Infrastructure.FinamV2;
using Trading.Core.Interfaces;

namespace TradingApp.Services;

public sealed class TradingRuntime : IDisposable
{
    private readonly FinamV2Host _host;
    private CancellationTokenSource? _cts;

    public string AccountId { get; private set; } = "";

    public TradingRuntime(string secret) => _host = new FinamV2Host(secret);

    public async Task StartAsync(IEnumerable<string> symbols, Action<string> log)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();

        var started = await _host.StartAsync(symbols, log, _cts.Token);
        AccountId = started.accountId;

        Observe(started.jwtTask, "jwt", log);
        Observe(started.tradeTask, "trade", log);
        Observe(started.bookTask, "book", log);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _host.Channel.Dispose();
    }

    public IMarketDataFeed MarketData => _host.OrderBookFeed;
    public ITradingGateway Trading => _host.Trading;

    private static void Observe(Task t, string name, Action<string> log)
    {
        t.ContinueWith(x =>
        {
            if (x.IsFaulted) log($"{name} faulted: {x.Exception}");
            else if (x.IsCanceled) log($"{name} canceled");
            else log($"{name} completed");
        }, TaskScheduler.Default);
    }
}
