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
using System.IO.Pipelines;

public class OrderBookScreenerService
{
    private readonly MarketDataStreamService.MarketDataStreamServiceClient _streamClient;
    private readonly Dictionary<string, string> _figiToTicker;
    private readonly HttpClient _http = new HttpClient();
    private readonly SettingsService _settings;
    private readonly TradeService _tradeService;

    public OrderBookScreenerService(MarketDataStreamService.MarketDataStreamServiceClient streamClient, SettingsService settings, TradeService tradeService)
    {
        _streamClient = streamClient;
        _settings = settings;
        _tradeService = tradeService;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-Api-Key", _settings.fToken);

        // Жестко заданные инструменты
        _figiToTicker = new Dictionary<string, string>
        {
            { "BBG004S68598", "MTLR" },
            { "BBG004730N88", "SBER" },
            { "BBG004730RP0", "GAZP" },
            { "BBG004731032", "LKOH" },
            { "BBG004S683W7", "AFLT" },
            { "BBG01JRXN2X9", "ASTR" },
            { "BBG000NLB2G3", "KROT" },
            { "BBG00JXPFBN0", "X5"   },
            { "BBG000TY1C41", "BELU" },
            { "BBG008F2T3T2", "RUAL" },
            { "BBG004730JJ5", "MOEX" },
            { "BBG004RVFCY3", "MGNT" },
            { "BBG004S68614", "AFKS" },
            { "BBG004S68B31", "ALRS" },
            { "BBG00475K2X9", "HYDR" },
            { "BBG000FR4JW2", "SVCB" },
            { "BBG004S681W1", "MTSS" },
        };
        _settings = settings;
    }


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
                    //new OrderBookInstrument{Figi="BBG004S68598",Depth=20}, // MTLR
                    //new OrderBookInstrument{Figi="BBG004S683W7",Depth=20}, // AFLT
                    //new OrderBookInstrument{Figi="BBG01JRXN2X9",Depth=20}, // ASTR (не работает)
                    //new OrderBookInstrument{Figi="BBG000NLB2G3",Depth=20}, // KROT
                    //new OrderBookInstrument{Figi="BBG00JXPFBN0",Depth=20}, // X5 (не работает)
                    //new OrderBookInstrument{Figi="BBG000TY1C41",Depth=20}, // BELU (не работает)
                    //new OrderBookInstrument{Figi="BBG008F2T3T2",Depth=20}, // RUAL
                    //new OrderBookInstrument{Figi="BBG004730JJ5",Depth=20}, // MOEX
                    //new OrderBookInstrument{Figi="BBG004RVFCY3",Depth=20}, // MGNT
                    //new OrderBookInstrument{Figi="BBG004S68614",Depth=20}, // AFKS
                    //new OrderBookInstrument{Figi="BBG004S68B31",Depth=20}, // ALRS
                    //new OrderBookInstrument{Figi="BBG00475K2X9",Depth=20}, // HYDR 
                    //new OrderBookInstrument{Figi="BBG000FR4JW2",Depth=20}, // SVCB (не работает) 
                    new OrderBookInstrument{Figi="BBG004S681W1",Depth=20}, // MTSS
                    //new OrderBookInstrument{Figi="BBG004730N88",Depth=20}, // SBER
                    //new OrderBookInstrument{Figi="BBG004730RP0",Depth=20}, // GAZP
                    //new OrderBookInstrument{Figi="BBG004731032",Depth=20}, // LKOH
                }
            }
        });

        DateTime lastRestCall = DateTime.MinValue;
        TimeSpan restInterval = TimeSpan.FromSeconds(2);

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

                    // Лимитирует вызов REST‑функции по времени
                    if (DateTime.UtcNow - lastRestCall >= restInterval)
                    {
                        lastRestCall = DateTime.UtcNow;

                        double avgVolume = await GetAverageVolumePer10MinAsync(ticker);
                        var lotSize = _settings.GetLotSize(ticker);
                        
                        // Проходит каждый уровень Bid
                        foreach (var lvl in ob.Bids)
                        {
                            long qty = lvl.Quantity;
                            double priceDouble = lvl.Price.Units + lvl.Price.Nano / 1_000_000_000.0;

                            double? density = qty * lotSize * priceDouble;

                            if (density >= avgVolume/10)
                            {
                                // лимитка купить на ценовом уровне выше кластера (+ шаг)
                                var buyPrice = priceDouble + _settings.GetTickSize(ticker);
                                var buyLots = 1; // или сколько надо лотов
                                var orderId = await _tradeService.PlaceLimitOrderAsync( // ставит заявку и возвращает orderId
                                      securityCode: ticker,
                                      price: buyPrice,
                                      quantity: buyLots,
                                      isBuy: true);

                                if (orderId != null)
                                {
                                    await MonitorOrderAsync(ob.Figi, priceDouble, 3500, orderId);
                                }

                                Debug.WriteLine(
                                    $"[СКРИННЕР] {ticker} BID‑кластер: {lvl.Quantity} лотов по цене {priceDouble}");

                                return;
                            }
                        }

                        /*
                        // И каждый уровень Ask
                        foreach (var lvl in ob.Asks)
                        {
                            long qty = lvl.Quantity;
                            double priceDouble = lvl.Price.Units + lvl.Price.Nano / 1_000_000_000.0;

                            double? density = qty * lotSize * priceDouble;

                            if (density >= avgVolume/10)
                            {
                                // лимитка продать на ценовом уровне выше кластера (- шаг)
                                var buyPrice = priceDouble - _settings.GetTickSize(ticker); 
                                var buyLots = 1; // или сколько надо лотов
                                _ = _tradeService.PlaceLimitOrderAsync(
                                      securityCode: ticker,
                                      price: buyPrice,
                                      quantity: buyLots,
                                      isBuy: false);

                                Debug.WriteLine(
                                    $"[СКРИННЕР] {ticker} ASK‑кластер: {lvl.Quantity} лотов по цене {priceDouble}");

                                return;
                            }
                        }
                        */

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

    // Вспомогательная: из "M15" → 15 минут, "M5" → 5 и т.д.
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



    // Слежка за объёмом на одном уровне стакана. Как только общий объём
    // на уровне <paramref name="price"/> упадёт ниже <paramref name="minLots"/>,
    // заявка <paramref name="orderId"/> будет отозвана, и слежка прекратится.
    public async Task MonitorOrderAsync(
        string figi,
        double price,
        long minLots,
        string orderId,
        CancellationToken ct = default)
    {
        var call = _streamClient.MarketDataStream();

        // подписываемся только на один инструмент
        await call.RequestStream.WriteAsync(new MarketDataRequest
        {
            SubscribeOrderBookRequest = new SubscribeOrderBookRequest
            {
                SubscriptionAction = SubscriptionAction.Subscribe,
                Instruments =
                {
                    new OrderBookInstrument
                    {
                        Figi = figi,
                        Depth = 20
                    }
                }
            }
        });

        try
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync(ct))
            {
                if (resp.PayloadCase != MarketDataResponse.PayloadOneofCase.Orderbook)
                    continue;

                var ob = resp.Orderbook;

                // вычисляем общий объём (в лотах) на точной цене
                long totalLots = 0;
                foreach (var lvl in ob.Bids)
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    if (Math.Abs(lvlPrice - price) < 1e-9)
                    {
                        if (lvl.Quantity >= minLots)
                            return; // Всё ещё есть защита — продолжаем ждать

                        break; // Уровень пустоват — снимаемся
                    }
                }
                foreach (var lvl in ob.Asks)
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    if (Math.Abs(lvlPrice - price) < 1e-9)
                    {
                        if (lvl.Quantity >= minLots)
                            return; // Всё ещё есть защита — продолжаем ждать

                        break; // Уровень пустоват — снимаемся
                    }
                }

                // если упало ниже порога — отменяем и выходим
                if (totalLots < minLots)
                {
                    Debug.WriteLine($"[MONITOR] price {price} lots={totalLots} < {minLots} — cancel {orderId}");
                    // await _tradeService.CancelOrderAsync(orderId);
                    return;
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            Debug.WriteLine("MonitorOrderAsync cancelled by token");
        }
    }
}