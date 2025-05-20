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
using System.Buffers.Text;

public class OrderBookScreenerService
{
    private readonly MarketDataStreamService.MarketDataStreamServiceClient _streamClient;
    private readonly Dictionary<string, string> _figiToTicker;
    private readonly HttpClient _http = new HttpClient();
    private readonly SettingsService _settings;

    public OrderBookScreenerService(MarketDataStreamService.MarketDataStreamServiceClient streamClient, SettingsService settings)
    {
        _streamClient = streamClient;
        _settings = settings;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-Api-Key", _settings.fToken);

        // Жестко заданные инструменты
        _figiToTicker = new Dictionary<string, string>
        {
            { "BBG004730N88", "MTLR" },
            { "BBG004S68598", "SBER" }
        };
        _settings = settings;
    }

    // Кусок скриннера (слушает все стаканы из массива)
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var call = _streamClient.MarketDataStream();

        // Подписка сразу на все нужные стаканы
        await call.RequestStream.WriteAsync(new MarketDataRequest
        {
            SubscribeOrderBookRequest = new SubscribeOrderBookRequest
            {
                SubscriptionAction = SubscriptionAction.Subscribe,
                Instruments =
                {
                    new OrderBookInstrument { Figi = "BBG004730N88", Depth = 20 },
                    new OrderBookInstrument { Figi = "BBG004S68598", Depth = 20 }
                }
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var resp in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (resp.PayloadCase != MarketDataResponse.PayloadOneofCase.Orderbook)
                        continue;

                    var ob = resp.Orderbook;
                    var ticker = _figiToTicker.GetValueOrDefault(ob.Figi) ?? ob.Figi;

                    if (ob.Bids.Count > 0)
                    {
                        var bestBid = ob.Bids[0];
                        var price = bestBid.Price.Units + bestBid.Price.Nano / 1e9;

                        if (bestBid.Quantity > 30000)
                        {
                            System.Diagnostics.Debug.WriteLine($"[СКРИННЕР] {ticker}: жирная покупка по {price} x {bestBid.Quantity}");
                        }
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Debug.WriteLine("Скриннер остановлен (отмена токена)");
            }
        }, cancellationToken);
    }


    // Возвращает средний объём за 5 минут по инструменту (сканирует все свечи по дню)
    public async Task<double> GetAverageVolumePer10MinAsync(string code)
    {
        const string CandlesEndpoint = ApiEndpoints.Candles;

        var from = DateTime.UtcNow.Date.AddHours(4);   // сегодня 07:00 UTC
        var to = DateTime.UtcNow;                    // сейчас
        var count = 200;
        var board = "TQBR";
        var timeframe = "M15";

        var url = $"{CandlesEndpoint}" +
                  $"?SecurityBoard={board}" +
                  $"&SecurityCode={code}" +
                  $"&TimeFrame={timeframe}" +
                  $"&Interval.From={Uri.EscapeDataString(from.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
                  $"&Interval.To={Uri.EscapeDataString(to.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
                  $"&Interval.Count={count}";

        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var candles = doc.RootElement
                         .GetProperty("data")
                         .GetProperty("candles")
                         .EnumerateArray();

        long totalVolume = 0;
        int candleCount = 0;
        int minutesPerCandle = ParseTimeframeMinutes(timeframe);

        foreach (var el in candles)
        {
            totalVolume += el.GetProperty("volume").GetInt64();
            candleCount++;
        }

        if (candleCount == 0 || minutesPerCandle == 0)
            return 0;


        // Берём последнюю свечу
        var listOfCandles = candles.ToList();
        var lastCandle = listOfCandles[^1];

        // Извлекаем close.num и close.scale
        var closeElem = lastCandle.GetProperty("close");
        long num = closeElem.GetProperty("num").GetInt64();
        int scale = closeElem.GetProperty("scale").GetInt32();
        // Преобразуем в реальную цену
        double lastPrice = num / Math.Pow(10, scale);

        double totalMinutes = candleCount * minutesPerCandle;
        double avgPerMinute = totalVolume * lastPrice / totalMinutes;
        return avgPerMinute * 10.0;
    }

    // Вспомогательный: из "M15" → 15 минут, "M5" → 5 и т.д.
    private int ParseTimeframeMinutes(string tf)
    {
        if (tf.StartsWith("M", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(tf.Substring(1), out var m))
        {
            return m;
        }
        // при необходимости добавить H1 → 60, D1 → 1440 и т.д.
        return 0;
    }
    
}