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
using TradingApp.Helpers;

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
            { "BBG004S68598", "MTLR" },
            { "BBG004730N88", "SBER" },
            { "BBG004730RP0", "GAZP" },
            { "BBG004731032", "LKOH" },
        };
        _settings = settings;
    }

    /*
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var call = _streamClient.MarketDataStream();

        await call.RequestStream.WriteAsync(new MarketDataRequest
        {
            SubscribeOrderBookRequest = new SubscribeOrderBookRequest
            {
                SubscriptionAction = SubscriptionAction.Subscribe,
                Instruments =
            {
                new OrderBookInstrument{Figi="BBG004S68598",Depth=20},
                new OrderBookInstrument{Figi="BBG004730N88",Depth=20},
                new OrderBookInstrument{Figi="BBG004730RP0",Depth=20},
                new OrderBookInstrument{Figi="BBG004731032",Depth=20}
            }
            }
        });

        DateTime lastRestCall = DateTime.MinValue;
        TimeSpan restInterval = TimeSpan.FromSeconds(1);

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

                    // Работа по каждому сообщению — лёгкая синхронная логика
                    var bestBid = ob.Bids.FirstOrDefault();
                    if (bestBid is null)
                        continue;

                    var price = bestBid.Price.Units + bestBid.Price.Nano / 1e9;

                    // Лимитируем вызов REST‑функции по времени
                    if (DateTime.UtcNow - lastRestCall >= restInterval)
                    {
                        lastRestCall = DateTime.UtcNow;

                        // здесь ты вызываешь тяжёлую работу, например GetAverageVolumePer10MinAsync
                        double avg10 = await GetAverageVolumePer10MinAsync(ticker);

                        // проверка уже с avg10
                        var lotSize = _settings.GetLotSize(ticker);
                        double? density = bestBid.Quantity * lotSize * price;

                        if (density > avg10)
                            Debug.WriteLine($"[СКРИННЕР] {ticker}: плотность {density:F0} > {avg10:F0}");
                    }
                    else  // для логирования лёгкой работы
                    {
                        if (bestBid.Quantity > 100)
                            Debug.WriteLine($"[СКРИННЕР] {ticker}: жирная покупка {price} x {bestBid.Quantity}");
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Debug.WriteLine("Скриннер остановлен (отмена токена)");
            }
        }, cancellationToken);
    }
    */


    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var call = _streamClient.MarketDataStream();

        await call.RequestStream.WriteAsync(new MarketDataRequest
        {
            SubscribeOrderBookRequest = new SubscribeOrderBookRequest
            {
                SubscriptionAction = SubscriptionAction.Subscribe,
                Instruments =
            {
                new OrderBookInstrument{Figi="BBG004S68598",Depth=20},
                new OrderBookInstrument{Figi="BBG004730N88",Depth=20},
                new OrderBookInstrument{Figi="BBG004730RP0",Depth=20},
                new OrderBookInstrument{Figi="BBG004731032",Depth=20}
            }
            }
        });

        DateTime lastRestCall = DateTime.MinValue;
        TimeSpan restInterval = TimeSpan.FromSeconds(1);

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

                    // Работа по каждому сообщению — лёгкая синхронная логика
                    var bestBid = ob.Bids.FirstOrDefault();
                    if (bestBid is null)
                        continue;

                    //   var price = bestBid.Price.Units + bestBid.Price.Nano / 1e9; //

                    // Лимитируем вызов REST‑функции по времени
                    if (DateTime.UtcNow - lastRestCall >= restInterval)
                    {
                        lastRestCall = DateTime.UtcNow;

                        double avgVolume = await GetAverageVolumePer10MinAsync(ticker);
                        var lotSize = _settings.GetLotSize(ticker);
                        //double? density = bestBid.Quantity * lotSize * price;

                        foreach (var lvl in ob.Bids)
                        {
                            long qty = lvl.Quantity;
                            double priceDouble = lvl.Price.Units + lvl.Price.Nano / 1_000_000_000.0;

                            double? density = qty * lotSize * priceDouble;

                            if (density >= avgVolume)
                            {
                                Debug.WriteLine(
                                    $"[СКРИННЕР] {ticker} BID‑кластер: {lvl.Quantity} лотов по цене {lvl.Price}");
                            }
                        }

                        // И каждый уровень Ask
                        foreach (var lvl in ob.Asks)
                        {
                            long qty = lvl.Quantity;
                            double priceDouble = lvl.Price.Units + lvl.Price.Nano / 1_000_000_000.0;

                            double? density = qty * lotSize * priceDouble;

                            if (density >= avgVolume)
                            {
                                Debug.WriteLine(
                                    $"[СКРИННЕР] {ticker} BID‑кластер: {lvl.Quantity} лотов по цене {lvl.Price}");
                            }
                        }

                    }
                    else  // для логирования лёгкой работы
                    {
                        //if (bestBid.Quantity > 100)
                        //    Debug.WriteLine($"[СКРИННЕР] {ticker}: жирная покупка {price} x {bestBid.Quantity}");
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Debug.WriteLine("Скриннер остановлен (отмена токена)");
            }
        }, cancellationToken);
    }


    // Возвращает средний объём за 30 минут по инструменту (сканирует все свечи по дню)
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
        return avgPerMinute * 30.0;
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