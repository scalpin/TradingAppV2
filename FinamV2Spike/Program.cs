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
var secret = "eyJraWQiOiJjYmMzZTFhMi1mNGYyLTQ4NDgtOGU2Yi1iNmYyZWJkZmY5ZjUiLCJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJhcmVhIjoidHQiLCJwYXJlbnQiOiJiM2Y4YWNiZC0yNmQwLTQwMGEtYWY4OS1kMDBiMTA2NzBiYWYiLCJhcGlUb2tlblByb3BlcnRpZXMiOiJINHNJQUFBQUFBQUFfeldUc1hMYk1BeUdIVXR1Zk82R01hTzc1YTUzY2RwZXJpTkZrYklhVVZKSXlwYTdhTW5jRG4yRVBrTmZ3SHZ2LWg1OXFmNEUyT1g3UVJJQUNRSGFfcjctX1huOVJPdHRjZmVuM0s3M3FfdmIzZWFkcjN3dGhxNDhHLWV6SHVfZjdFcmw1b0hWNnpscHBXSmdQVG1iMU15alM5cWF1V0cxWmhLZDJNLTFnZU9HSVdyVzBTdldxS3VzTHV0TDBySHRkZFkyYTVlVjg0eWhZWDhmSmEtZlJHZmxnbWd3V1dXX0dweW9uMFFuT2RlVnpScEZqUloxY202aVo3WHkzcmxSNG5kOHRsbTUzcm1yZTlFYzEtWDd1a0gybmFwWmV5WHY2NlhldVdfeU9yLTd2LVQ5aTZ4SEpmZUhvOGtxOThYbmtCcTBWTDNXYk9pcVpjOUZveU84NFM4akcyYnlGUnZPVlZfWjZFZlAzVnlDYm5rak9LZnVON3RpQ1JJU3h1d1p4dGNERzlHYjhIaXp1dDZzZDZ1N1gtVzJvRUtaR1FnbTRTVUJTOHdGOEpLUUR2Q1ZxYXktZjN1bFRlSVBLdkM5RS1DZ2p5M2dUQUlpOWNuUWJmMmw3ZXVVMWt3ejhWRFJyYlZ1Y2Y1QXBXMTdSYnRFdDlSVHZOREdEdWdfRlJaWHJTMmlHdU9wT0Zxa1JGdW9hTTBFVFBEbzlBVklkM1hwV1dnR29CSGhjRm1KNHZFVTUwOVVvRDJBU3dodGdrdEFkZWhMQWx1QmVJNnBHSUlpSG1HbVk4SURQU09lWUdiTDdKaTRiSHdPQi1ZSDVrZTQ0MGs4MEZSNGpRX2pZMFViUC1FWHBDS29BVEFoQVVlaFFRYmdQY3pqRVdnUkZDSUtDV2NnMXFnNmRpY3FJMnFDT1NCRnZDREZwSEI4VHJYUENDblQySkFNVDFwRXkwUmRpMDVscEJraW1TQ1MtY0ZlVXl1U0dTSVpFSktwMktfLXo4WC03ZFBEMC1Iemc2X2M5UGdQbktDc3dHTUVBQUEiLCJzY29udGV4dCI6IkNoQUlCeElNZEhKaFpHVmZZWEJwWDNKMUNpZ0lBeElrTnpZMU4yVmlaREF0WWpaaU5DMDBPR0l3TFdGbU16RXRZekF3WldZeFkyVmlaRFF4Q2dRSUJSSUFDZ2tJQUJJRmFIUnRiRFVLS0FnQ0VpUXhNak13WldabU1DMHhZelptTFRFeFpqQXRZbUl5TWkweE5XUTVOVFZoWlRGall6Z0tCUWdJRWdFekNnUUlDUklBQ2drSUNoSUZNUzQyTGpZS0tBZ0VFaVJqWW1NelpURmhNaTFtTkdZeUxUUTRORGd0T0dVMllpMWlObVl5WldKa1ptWTVaalV5VUFvVlZGSkJSRVZCVUVsZlMxSkJWRTlUWDFSUFMwVk9FQUVZQVNBQktnZEZSRTlZWDBSQ09nSUlBMG9UQ2dNSWh3Y1NCUWlIb1o0QkdnVUloNWJEQVZDc0FsZ0JZQUZvQVhJR1ZIaEJkWFJvIiwiemlwcGVkIjp0cnVlLCJjcmVhdGVkIjoiMTc2ODEzMzUzMSIsInJlbmV3RXhwIjoiMTgzMDIwMDQ2MyIsInNlc3MiOiJINHNJQUFBQUFBQUEvMU5xWStSU01VZ3lOalF5TURmU05iUXdzZEExTVRFeDFrMnlOREhSVFV1eU5ESk9TVFJOTkVneUV1SzVNT3ZDam9zTkYzWmMySDFodDVUUWhRa1hOZ0o1K3k0Mlh1eTVzQXNvdWxkSkxER3BLTDg0T3pNdlc4L1V3aUU5TnpFelJ5ODVQemRKeGRYUjBkak56TWxOMThEUXdGTFh4TTNjWE5mSkVHaVRvNm1iczdHbHNadUpvYm5yTGtaZUx0WjR2NEFnZnlFV2Z5Zi9DQUFyTmVsK21RQUFBQSIsImlzcyI6InR4c2VydmVyIiwia2V5SWQiOiJjYmMzZTFhMi1mNGYyLTQ4NDgtOGU2Yi1iNmYyZWJkZmY5ZjUiLCJ0eXBlIjoiQXBpVG9rZW4iLCJzZWNyZXRzIjoiaTFiNkFYRlhFTC9EbHRoQXRvNFhjQT09Iiwic2NvcGUiOiIiLCJ0c3RlcCI6ImZhbHNlIiwic3BpblJlcSI6ZmFsc2UsImV4cCI6MTgzMDIwMDQwMywic3BpbkV4cCI6IjE4MzAyMDA0NjMiLCJqdGkiOiI3NjU3ZWJkMC1iNmI0LTQ4YjAtYWYzMS1jMDBlZjFjZWJkNDEifQ.EFH3SAGZlFqpDFivSH7xWG9KFVxCdOO1RgNzGPbCCRRESCMsie37uer_rYA8HFmZdwzeHesfnvmwrU3vEaD2zg"; 
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
