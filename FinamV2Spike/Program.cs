using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;

// эти using скорее всего придётся поправить под реальные namespaces
using Grpc.Tradeapi.V1.Auth;
using Grpc.Tradeapi.V1.Orders;
using Grpc.Tradeapi.V1.Marketdata;

static Metadata AuthHeader(string jwt) =>
    new() { { "authorization", $"Bearer {jwt}" } };

static GrpcChannel CreateChannel()
{
    var handler = new SocketsHttpHandler
    {
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        EnableMultipleHttp2Connections = true
    };

    // gRPC endpoint api.finam.ru:443 
    return GrpcChannel.ForAddress("https://api.finam.ru", new GrpcChannelOptions
    {
        HttpHandler = handler
    });
}

// именно secret, не jwt
var secret = Environment.GetEnvironmentVariable("FINAM_SECRET");
if (string.IsNullOrWhiteSpace(secret))
{
    Console.WriteLine("secret is empty");
    return;
}

using var channel = CreateChannel();
var auth = new AuthService.AuthServiceClient(channel);
var orders = new OrdersService.OrdersServiceClient(channel);
var md = new MarketDataService.MarketDataServiceClient(channel);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

string jwt;

// 1) получить JWT 
{
    var resp = await auth.AuthAsync(new AuthRequest { Secret = secret }, cancellationToken: cts.Token);
    jwt = resp.Token;
    Console.WriteLine($"jwt acquired, len={jwt.Length}");
}

// 2) запустить обновление JWT через стрим 
_ = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            using var call = auth.SubscribeJwtRenewal(
                new SubscribeJwtRenewalRequest { Secret = secret },
                cancellationToken: cts.Token
            );

            await foreach (var msg in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (!string.IsNullOrWhiteSpace(msg.Token))
                {
                    jwt = msg.Token;
                    Console.WriteLine($"jwt renewed, len={jwt.Length}");
                }
            }
        }
        catch (RpcException ex) when (!cts.IsCancellationRequested)
        {
            Console.WriteLine($"jwt renewal rpc error: {ex.StatusCode} {ex.Status.Detail}");
            await Task.Delay(500, cts.Token);
        }
        catch (Exception ex) when (!cts.IsCancellationRequested)
        {
            Console.WriteLine($"jwt renewal error: {ex.GetType().Name}: {ex.Message}");
            await Task.Delay(500, cts.Token);
        }
    }
}, cts.Token);

// 3) взять account_id через TokenDetails 
string accountId;
{
    var details = await auth.TokenDetailsAsync(new TokenDetailsRequest { Token = jwt }, cancellationToken: cts.Token);
    if (details.AccountIds.Count == 0)
    {
        Console.WriteLine("no account_ids in token");
        return;
    }
    accountId = details.AccountIds[0];
    Console.WriteLine($"using account_id: {accountId}");
}

// 4) подписка на свои заявки+сделки: SubscribeOrderTrade 
_ = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            using var call = orders.SubscribeOrderTrade(headers: AuthHeader(jwt), cancellationToken: cts.Token);

            await call.RequestStream.WriteAsync(new OrderTradeRequest
            {
                AccountId = accountId,
                Action = (OrderTradeRequest.Types.Action)0,   // ACTION_SUBSCRIBE
                DataType = (OrderTradeRequest.Types.DataType)0 // DATA_TYPE_ALL
            });

            await foreach (var msg in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                foreach (var o in msg.Orders)
                    Console.WriteLine($"order: id={o.OrderId} status={o.Status}");

                foreach (var t in msg.Trades)
                    Console.WriteLine($"trade: id={t.TradeId} symbol={t.Symbol} side={t.Side} price={t.Price?.Value} size={t.Size?.Value}");
            }
        }
        catch (RpcException ex) when (!cts.IsCancellationRequested)
        {
            Console.WriteLine($"ordertrade rpc error: {ex.StatusCode} {ex.Status.Detail}");
            await Task.Delay(500, cts.Token);
        }
        catch (Exception ex) when (!cts.IsCancellationRequested)
        {
            Console.WriteLine($"ordertrade error: {ex.GetType().Name}: {ex.Message}");
            await Task.Delay(500, cts.Token);
        }
    }
}, cts.Token);

// 5) подписка на стакан: SubscribeOrderBook 
var symbol = "SBER@MISX"; // формат TICKER@MIC 

while (!cts.IsCancellationRequested)
{
    try
    {
        using var call = md.SubscribeOrderBook(
            new SubscribeOrderBookRequest { Symbol = symbol },
            AuthHeader(jwt),
            cancellationToken: cts.Token
        );

        await foreach (var msg in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            foreach (var book in msg.OrderBook)
            {
                foreach (var row in book.Rows)
                {
                    var p = row.Price?.Value ?? "?";
                    var b = row.BuySize?.Value;
                    var s = row.SellSize?.Value;
                    Console.WriteLine($"ob {symbol} {row.Action} p={p} b={b} s={s}");
                }
            }
        }
    }
    catch (RpcException ex) when (!cts.IsCancellationRequested)
    {
        Console.WriteLine($"orderbook rpc error: {ex.StatusCode} {ex.Status.Detail}");
        await Task.Delay(500, cts.Token);
    }
    catch (Exception ex) when (!cts.IsCancellationRequested)
    {
        Console.WriteLine($"orderbook error: {ex.GetType().Name}: {ex.Message}");
        await Task.Delay(500, cts.Token);
    }
}
