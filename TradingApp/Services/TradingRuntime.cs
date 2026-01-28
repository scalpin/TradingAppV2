//TradingRuntime.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trading.Infrastructure.FinamV2.Trading;
using Trading.Infrastructure.FinamV2.MarketData;
using Trading.Infrastructure.FinamV2;
using Trading.Core.Interfaces;

namespace TradingApp.Services;

public sealed class TradingRuntime : IDisposable
{
    private readonly FinamV2Host _host;
    private CancellationTokenSource? _cts;

    public string AccountId { get; private set; } = "";

    public IMarketDataFeed MarketData => _host.OrderBookFeed;
    public ITradingGateway Trading => _host.Trading;

    // вот это тебе и нужно
    public ILiquidityProvider Liquidity => _host.Liquidity;

    public TradingRuntime(string secret)
    {
        _host = new FinamV2Host(secret);
    }

    public async Task StartAsync(IEnumerable<string> symbols, Action<string> log)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();

        var started = await _host.StartAsync(symbols, log, _cts.Token);
        AccountId = started.accountId;

        // не await, просто держим живыми
        _ = started.jwtTask;
        _ = started.tradeTask;
        _ = started.bookTask;

        // если ты добавил liqTask в StartAsync — тоже
        // _ = started.liqTask;
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
}