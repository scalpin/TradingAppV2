//FinamOrderBookFeed.cs
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

    private const int Depth = 20;
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(250);

    public FinamOrderBookFeed(GrpcChannel channel, JwtProvider jwt)
    {
        _md = new MarketDataService.MarketDataServiceClient(channel);
        _jwt = jwt;
    }

    public async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var list = symbols.Distinct().ToArray();
        foreach (var s in list)
            _caches.TryAdd(s, new OrderBookCache());

        var streamTasks = list.Select(s => RunSymbolStreamAsync(s, ct)).ToArray();
        var publishTask = PublishLoopAsync(list, ct);

        await Task.WhenAll(streamTasks.Append(publishTask));
    }

    private async Task PublishLoopAsync(string[] symbols, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PublishInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var symbol in symbols)
            {
                if (!_caches.TryGetValue(symbol, out var cache))
                    continue;

                if (!cache.TryBuildSnapshot(Depth, out var bids, out var asks))
                    continue;

                OrderBook?.Invoke(new OrderBookSnapshot(
                    symbol,
                    DateTimeOffset.UtcNow,
                    bids,
                    asks
                ));
            }
        }
    }

    private async Task RunSymbolStreamAsync(string symbol, CancellationToken ct)
    {
        var cache = _caches.GetOrAdd(symbol, _ => new OrderBookCache());

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
                        foreach (StreamOrderBook.Types.Row row in book.Rows)
                            cache.ApplyRow(row);
                }
            }
            catch (RpcException) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(300, ct);
            }
        }
    }
}
