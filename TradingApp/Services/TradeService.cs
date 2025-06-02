using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TradingApp.Constants;
using TradingApp.Services;
using TradingApp.Models;
using System.Net.Http.Json;
using Grpc.Net.Client;
using System.Collections.Generic;
using System.Linq;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;
using System.Threading;
using Grpc.Core;
using static Google.Rpc.Context.AttributeContext.Types;
using System.IO;
using System.Diagnostics;
using System.Threading.Channels;
using System.Windows.Controls;
using OrderModel = TradingApp.Models.Order;

public class TradeService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private readonly InvestApiClient _client;
    public MarketDataStreamService.MarketDataStreamServiceClient MarketDataStreamClient => _client.MarketDataStream;

    public TradeService(SettingsService settingsService)
    {
        _httpClient = new HttpClient();
        _settings = settingsService;

        // Добавляем заголовок с токеном при инициализации
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.Settings.fToken);
        _client = InvestApiClientFactory.Create(_settings.Settings.tToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    // Рабочий тейк профит
    public async Task<bool> PlaceTakeProfitOrderAsync(string board, string code, double activationPrice)
    {
        var endpoint = ApiEndpoints.PlaceStopOrder;

        var requestBody = new
        {
            clientId = _settings.Settings.ClientId,
            securityBoard = board,
            securityCode = code,
            buySell = "Buy",
            takeProfit = new
            {
                activationPrice = activationPrice,
                price = 0,
                marketPrice = true,
                quantity = new
                {
                    value = 1,
                    units = "Lots"
                },
                time = 0,
                useCredit = true
            },
            stopLoss = (object)null,
            validBefore = new
            {
                type = "TillEndSession",
                time = "2025-04-24T10:06:40Z"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var response = await _httpClient.PostAsync(endpoint, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return true;

        Console.ForegroundColor = ConsoleColor.Red;
        System.Diagnostics.Debug.WriteLine($"Ошибка при отправке заявки:");
        System.Diagnostics.Debug.WriteLine($"StatusCode: {response.StatusCode}");
        System.Diagnostics.Debug.WriteLine($"Response: {responseBody}");
        Console.ResetColor();
        return false;
    }


    // получение фотки стакана из тинькоффа (работает)
    public async Task GetOrderBookAsync(string figi)
    {
        var token = _settings.Settings.tToken;
        var endpoint = ApiEndpoints.GetOrderBook;

        var channel = GrpcChannel.ForAddress(endpoint);
        var client = new MarketDataService.MarketDataServiceClient(channel);

        var headers = new Grpc.Core.Metadata
        {
            { "Authorization", $"Bearer {token}" }
        };

        try
        {
            var request = new GetOrderBookRequest
            {
                Figi = figi,
                Depth = 10
            };

            var response = await client.GetOrderBookAsync(request, headers);

            foreach (var bid in response.Bids)
            {
                var price = bid.Price.Units + bid.Price.Nano / 1_000_000_000.0;
                System.Diagnostics.Debug.WriteLine($"Bid: {price} x {bid.Quantity}");
            }

            foreach (var ask in response.Asks)
            {
                var price = ask.Price.Units + ask.Price.Nano / 1_000_000_000.0;
                System.Diagnostics.Debug.WriteLine($"Ask: {price} x {ask.Quantity}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при получении стакана: {ex.Message}");
        }
    }


 
    // Подписывается на real‑time стакан по FIGI, вызывает onUpdate при каждом обновлении
    public async Task SubscribeOrderBookAsync(
        string figi,
        int depth,
        Action<OrderBook>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var call = _client.MarketDataStream.MarketDataStream();

        // отпрака запроса на подписку
        await call.RequestStream.WriteAsync(new MarketDataRequest
        {
            SubscribeOrderBookRequest = new SubscribeOrderBookRequest
            {
                SubscriptionAction = SubscriptionAction.Subscribe,
                Instruments =
                    {
                        new OrderBookInstrument { Figi = figi, Depth = depth }
                    }
            }
        });

        // чтение и либо лог, либо колбэк
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var resp in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (resp.PayloadCase != MarketDataResponse.PayloadOneofCase.Orderbook)
                        continue;

                    var ob = resp.Orderbook;

                    if (onUpdate != null)
                    {
                        onUpdate(ob);
                    }
                    else
                    {
                        // стандартный лог
                        var sb = new StringBuilder();
                        sb.AppendLine("=== BIDS ===");
                        foreach (var b in ob.Bids)
                        {
                            var price = b.Price.Units + b.Price.Nano / 1e9;
                            sb.AppendLine($"Bid: {price} x {b.Quantity}");
                        }
                        sb.AppendLine("=== ASKS ===");
                        foreach (var a in ob.Asks)
                        {
                            var price = a.Price.Units + a.Price.Nano / 1e9;
                            sb.AppendLine($"Ask: {price} x {a.Quantity}");
                        }
                        System.Diagnostics.Debug.WriteLine(sb.ToString());
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // норм завершение
            }
        }, cancellationToken);
    }

    // Возвращает массив с данными об активных заявках
    public async Task<List<OrderModel>> GetActiveOrdersAsync()
    {
        var url = $"https://trade-api.finam.ru/api/v1/orders" +
                  $"?ClientId={_settings.Settings.ClientId}" +
                  $"&IncludeMatched=false&IncludeCanceled=false&IncludeActive=true";
        var resp = await _httpClient.GetAsync(url);
        var raw = await resp.Content.ReadAsStringAsync();
        Debug.WriteLine($"[GetActiveOrders] {resp.StatusCode} {raw}");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.GetProperty("data");

        if (!root.TryGetProperty("orders", out var arrEl)
            || arrEl.ValueKind != JsonValueKind.Array)
        {
            Debug.WriteLine("[GetActiveOrders] orders не найден или не массив");
            return new();
        }

        var list = new List<OrderModel>();
        foreach (var el in arrEl.EnumerateArray())
        {
            list.Add(new OrderModel
            {
                OrderNo = el.GetProperty("orderNo").GetInt64(),
                TransactionId = el.GetProperty("transactionId").GetInt64(),
                SecurityCode = el.GetProperty("securityCode").GetString()!,
                SecurityBoard = el.GetProperty("securityBoard").GetString()!,
                BuySell = el.GetProperty("buySell").GetString()!,
                Price = el.GetProperty("price").GetDouble(),
                Quantity = el.GetProperty("quantity").GetInt32(),
                Status = el.GetProperty("status").GetString()!
            });
        }
        return list;
    }

    // Возвращает массив с данными об исполненных заявках
    public async Task<List<OrderModel>> GetMatchedOrdersAsync()
    {
        var url = $"https://trade-api.finam.ru/api/v1/orders" +
                  $"?ClientId={_settings.Settings.ClientId}" +
                  $"&IncludeMatched=true&IncludeCanceled=false&IncludeActive=false";
        var resp = await _httpClient.GetAsync(url);
        var raw = await resp.Content.ReadAsStringAsync();
        Debug.WriteLine($"[GetMatchedOrders] {resp.StatusCode} {raw}");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.GetProperty("data");

        if (!root.TryGetProperty("orders", out var arrEl)
            || arrEl.ValueKind != JsonValueKind.Array)
        {
            Debug.WriteLine("[GetMatchedOrders] orders не найден или не массив");
            return new();
        }

        var list = new List<OrderModel>();
        foreach (var el in arrEl.EnumerateArray())
        {
            list.Add(new OrderModel
            {
                OrderNo = el.GetProperty("orderNo").GetInt64(),
                TransactionId = el.GetProperty("transactionId").GetInt64(),
                SecurityCode = el.GetProperty("securityCode").GetString()!,
                SecurityBoard = el.GetProperty("securityBoard").GetString()!,
                BuySell = el.GetProperty("buySell").GetString()!,
                Price = el.GetProperty("price").GetDouble(),
                Quantity = el.GetProperty("quantity").GetInt32(),
                Status = el.GetProperty("status").GetString()!
            });
        }
        return list;
    }

    //Лимитная заявка (возвращает TransactionId)
    public async Task<long> PlaceLimitOrderAsync(
        string securityCode,
        bool isBuy,
        double price,
        int quantity
    )
    {
        // 1) выставляем заявку
        var endpoint = ApiEndpoints.PlaceLimitOrder;
        var body = new
        {
            clientId = _settings.Settings.ClientId,
            securityBoard = "TQBR",
            securityCode = securityCode,
            buySell = isBuy ? "Buy" : "Sell",
            price = price,
            quantity = quantity,
            useCredit = true,
            timeInForce = "Day",
            property = "PutInQueue"
        };

        var json = JsonSerializer.Serialize(body);
        var resp = await _httpClient.PostAsync(endpoint,
                             new StringContent(json, Encoding.UTF8, "application/json-patch+json"));
        var respBody = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Debug.WriteLine($"Order error: {resp.StatusCode} / {respBody}");
            return 0;
        }

        // 2) даём API секунду на появление
        await Task.Delay(500);

        // 3) запрашиваем список активных ордеров
        var all = await GetActiveOrdersAsync();

        // 4) ищем последний ордер с такими же параметрами
        var match = all
            .Where(o => o.SecurityCode == securityCode
                     && o.BuySell == (isBuy ? "Buy" : "Sell")
                     && Math.Abs(o.Price - price) < 1e-9
                     && o.Quantity == quantity
                     && o.Status == "Active"   // или какой статус актуален
                  )
            .OrderByDescending(o => o.TransactionId) // или по какому-то другому полю времени
            .FirstOrDefault();

        if (match == null)
        {
            Debug.WriteLine("Не удалось найти новый ордер в списке");
            return 0;
        }

        Debug.WriteLine($"Found new TransactionId = {match.TransactionId}");
        return match.TransactionId;
    }

    public async Task PlaceMarketOrderAsync(
        string securityCode,
        int quantity,
        bool isBuy)
    {
        const string board = "TQBR";
        var endpoint = ApiEndpoints.PlaceLimitOrder; 

        var body = new
        {
            clientId = _settings.Settings.ClientId,
            securityBoard = board,
            securityCode = securityCode,
            buySell = isBuy ? "Buy" : "Sell",
            quantity = quantity,
            useCredit = true,
            property = "PutInQueue"
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var resp = await _httpClient.PostAsync(endpoint, content);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[PlaceMarketOrder] Ошибка: {resp.StatusCode} / {raw}");
        }
    }

    public async Task<bool> CancelOrderAsync(long transactionId)
    {
        // Собираем URL точно по образцу из Swagger
        var url = $"{ApiEndpoints.DeleteOrder}" +
                  $"?ClientId={_settings.Settings.ClientId}" +
                  $"&TransactionId={transactionId}";

        var resp = await _httpClient.DeleteAsync(url);
        if (resp.IsSuccessStatusCode)
            return true;

        var err = await resp.Content.ReadAsStringAsync();
        Debug.WriteLine($"CancelOrder error: {resp.StatusCode} / {err}");
        return false;
    }

}