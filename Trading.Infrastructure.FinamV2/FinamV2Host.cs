//FinamV2Host.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Trading.Infrastructure.FinamV2.FinamGrpc;
using Trading.Infrastructure.FinamV2.MarketData;
using Trading.Infrastructure.FinamV2.Trading;

namespace Trading.Infrastructure.FinamV2;

public sealed class FinamV2Host
{
    public GrpcChannel Channel { get; }
    public JwtProvider Jwt { get; }
    public FinamOrderBookFeed OrderBookFeed { get; }
    public FinamTradingGateway Trading { get; }

    public FinamV2Host(string secret)
    {
        Channel = FinamChannelFactory.Create();
        Jwt = new JwtProvider(Channel, secret);
        OrderBookFeed = new FinamOrderBookFeed(Channel, Jwt);
        Trading = new FinamTradingGateway(Channel, Jwt);
    }

    public async Task<(string accountId, Task jwtTask, Task tradeTask, Task bookTask)> StartAsync(
        IEnumerable<string> symbols,
        Action<string> log,
        CancellationToken ct)
    {
        var accountId = await Jwt.GetAccountIdAsync(ct);

        var jwtTask = Jwt.StartRenewalLoopAsync(log, ct);
        var tradeTask = Trading.StartAsync(accountId, log, ct);
        var bookTask = OrderBookFeed.StartAsync(symbols, ct);

        return (accountId, jwtTask, tradeTask, bookTask);
    }
}