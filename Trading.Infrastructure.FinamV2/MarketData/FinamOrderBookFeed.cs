using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;

using Trading.Core.Interfaces;
using Trading.Core.Models;
using Trading.Infrastructure.FinamV2.FinamGrpc;

using Grpc.Tradeapi.V1.Marketdata;

namespace Trading.Infrastructure.FinamV2.MarketData;

public sealed class FinamOrderBookFeed : IMarketDataFeed
{
    public event Action<OrderBookSnapshot>? OrderBook;

    private readonly MarketDataService.MarketDataServiceClient _md;
    private readonly JwtProvider _jwt;

    private readonly ConcurrentDictionary<string, OrderBookCache> _caches = new();

    public FinamOrderBookFeed(GrpcChannel channel, JwtProvider jwt)
    {
        _md = new MarketDataService.MarketDataServiceClient(channel);
        _jwt = jwt;
    }

    public async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var tasks = symbols.Select(symbol => RunSymbolAsync(symbol, ct)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task RunSymbolAsync(string symbol, CancellationToken ct)
    {
        var cache = _caches.GetOrAdd(symbol, _ => new OrderBookCache());

        // снапшоты раз в 100мс
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var snapshotTask = Task.Run(async () =>
        {
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    var top = cache.Top(20);

                    OrderBook?.Invoke(new OrderBookSnapshot(
                        symbol,
                        DateTimeOffset.UtcNow,
                        top.bids.Select(x => new Level(x.p, x.s)).ToList(),
                        top.asks.Select(x => new Level(x.p, x.s)).ToList()
                    ));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // норм
            }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _jwt.EnsureJwtAsync(ct);
                    var headers = _jwt.GetHeaders();

                    using var call = _md.SubscribeOrderBook(
                        new SubscribeOrderBookRequest { Symbol = symbol },
                        headers,
                        cancellationToken: ct);

                    await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
                    {
                        foreach (StreamOrderBook book in msg.OrderBook)
                        {
                            foreach (StreamOrderBook.Types.Row row in book.Rows)
                            {
                                cache.ApplyRow(row);
                            }
                        }
                    }
                }
                catch (RpcException) when (!ct.IsCancellationRequested)
                {
                    await Task.Delay(300, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // норм
        }

        await snapshotTask;
    }
}
