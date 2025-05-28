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
using System.Transactions;

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
            { "BBG004S684M6", "SIBN" },
            { "BBG004RVFFC0", "TATN" },
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
                    new OrderBookInstrument{Figi="BBG004S68B31",Depth=20}, // ALRS
                    //new OrderBookInstrument{Figi="BBG00475K2X9",Depth=20}, // HYDR 
                    //new OrderBookInstrument{Figi="BBG000FR4JW2",Depth=20}, // SVCB (не работает) 
                    //new OrderBookInstrument{Figi="BBG004S681W1",Depth=20}, // MTSS
                    //new OrderBookInstrument{Figi="BBG004730N88",Depth=20}, // SBER
                    //new OrderBookInstrument{Figi="BBG004730RP0",Depth=20}, // GAZP
                    //new OrderBookInstrument{Figi="BBG004731032",Depth=20}, // LKOH
                    //new OrderBookInstrument{Figi="BBG004S684M6",Depth=20}, // SIBN
                    //new OrderBookInstrument{Figi="BBG004RVFFC0",Depth=20}, // TATN
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

                        double avgVolume = await GetAverageVolumePer10MinAsync(ticker)/7;
                        var lotSize = _settings.GetLotSize(ticker);
                        

                        // Проходит каждый уровень Bid
                        foreach (var lvl in ob.Bids)
                        {
                            long qty = lvl.Quantity;
                            double priceDouble = lvl.Price.Units + lvl.Price.Nano / 1_000_000_000.0;

                            double? density = qty * lotSize * priceDouble;

                            if (density >= avgVolume)
                            {
                                Debug.WriteLine($"[СКРИННЕР] {ticker} BID‑кластер: {lvl.Quantity} лотов по цене {priceDouble}");

                                // лимитка купить на ценовом уровне выше кластера (+ шаг)
                                var buyPrice = priceDouble + _settings.GetTickSize(ticker);
                                var buyLots = 1;
                                var TransactionId = await _tradeService.PlaceLimitOrderAsync( // ставит заявку и возвращает TransactionId
                                      securityCode: ticker,
                                      price: buyPrice,
                                      quantity: buyLots,
                                      isBuy: true);

                                if (TransactionId != 0)
                                {
                                    //Запускает слежку по объёму кластера
                                    bool isMatched = await MonitorOrderCancelAsync(ob.Figi, priceDouble, Convert.ToInt64(lvl.Quantity / 2), TransactionId);

                                    // проверяет, исполнилась ли заявка
                                    if (isMatched)
                                    {
                                        Debug.WriteLine($"[AUTO] Order {TransactionId} is matched – continue process");

                                        int addedTicks = Convert.ToInt32((0.25 / (_settings.GetTickSize(ticker) / priceDouble * 100)));
                                        // Ставится лимитка на фиксацию прибыли
                                        var TransactionId2 = await _tradeService.PlaceLimitOrderAsync(
                                            securityCode: ticker,
                                            price: buyPrice + _settings.GetTickSize(ticker) * addedTicks,
                                            quantity: buyLots,
                                            isBuy: false);
                                        // Здесь вызов следующей функции //
                                        await MonitorOrderMarketAsync(
                                            ob.Figi, priceDouble, 
                                            buyLots,
                                            false,
                                            Convert.ToInt64(lvl.Quantity / 2), 
                                            TransactionId2);
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"[AUTO] Order {TransactionId} is cancelled, finish");
                                    }
                                }

                                return;
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
                                Debug.WriteLine($"[СКРИННЕР] {ticker} ASK‑кластер: {lvl.Quantity} лотов по цене {priceDouble}");

                                // лимитка продать на ценовом уровне выше кластера (- шаг)
                                var buyPrice = priceDouble - _settings.GetTickSize(ticker); 
                                var buyLots = 1; // или сколько надо лотов
                                var TransactionId = await _tradeService.PlaceLimitOrderAsync( // ставит заявку и возвращает TransactionId
                                      securityCode: ticker,
                                      price: buyPrice,
                                      quantity: buyLots,
                                      isBuy: false);

                                if (TransactionId != 0)
                                {
                                    //Запускает слежку по объёму кластера
                                    bool isMatched = await MonitorOrderCancelAsync(ob.Figi, priceDouble, Convert.ToInt64(lvl.Quantity / 2), TransactionId);

                                    // проверяет, исполнилась ли заявка
                                    if (isMatched)
                                    {
                                        Debug.WriteLine($"[AUTO] Order {TransactionId} is matched – continue process");
                                        int addedTicks = Convert.ToInt32((0.25 / (_settings.GetTickSize(ticker) / priceDouble * 100)));

                                        // Ставится лимитка на фиксацию прибыли
                                        var TransactionId2 = await _tradeService.PlaceLimitOrderAsync(
                                            securityCode: ticker,
                                            price: buyPrice - _settings.GetTickSize(ticker) * addedTicks,
                                            quantity: buyLots,
                                            isBuy: true);

                                        // Здесь вызов следующей функции //
                                        await MonitorOrderMarketAsync(
                                            ob.Figi, priceDouble,
                                            buyLots,
                                            true,
                                            Convert.ToInt64(lvl.Quantity / 2),
                                            TransactionId2);
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"[AUTO] Order {TransactionId} is cancelled, finish");
                                    }
                                }

                                return;
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

    //Рабочая слежка за целевой ценой с отменой заявки
    public async Task<bool> MonitorOrderCancelAsync(
    string figi,
    double price,
    long minLots,
    long transactionId,
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
                new OrderBookInstrument { Figi = figi, Depth = 20 }
            }
            }
        });

        // допустимая погрешность сравнения цен
        //var tol = _settings.GetTickSize(figi);
        var lastCheckTime = DateTime.MinValue; // Время последнего запроса
        bool isOrderActive = true; // Кешированный статус заявки

        try
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync(ct))
            {
                // 1) Сначала проверяем, остался ли ордер активным
                if ((DateTime.UtcNow - lastCheckTime).TotalSeconds >= 1.5)
                {
                    lastCheckTime = DateTime.UtcNow;
                    var activeOrders = await _tradeService.GetActiveOrdersAsync();
                    isOrderActive = activeOrders.Any(o => o.TransactionId == transactionId);
                }
                if (!isOrderActive)
                {
                    Debug.WriteLine($"[MONITOR] Order {transactionId} no longer active – stop monitoring");
                    // либо исполнена, либо отменена
                    var matched = (await _tradeService.GetMatchedOrdersAsync())
                        .Any(o => o.TransactionId == transactionId);
                    return matched;
                }

                if (resp.PayloadCase != MarketDataResponse.PayloadOneofCase.Orderbook)
                    continue;

                var ob = resp.Orderbook;

                // ищем нужный уровень в Bids
                var bidLvl = ob.Bids.FirstOrDefault(lvl =>
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    return lvlPrice == price;
                });

                if (bidLvl != null)
                {
                    // если объём упал ниже минимума — отменяем и выходим
                    if (bidLvl.Quantity < minLots)
                    {
                        await _tradeService.CancelOrderAsync(transactionId);
                        Debug.WriteLine($"[MONITOR] Отменена заявка {transactionId} on {figi} @ {price}");
                        return false;
                    }
                    // иначе — уровень ещё держится, ждём следующий снимок
                    continue;
                }

                // если не нашли в Bids — проверяем Asks
                var askLvl = ob.Asks.FirstOrDefault(lvl =>
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    return lvlPrice == price;
                });

                if (askLvl != null)
                {
                    if (askLvl.Quantity < minLots)
                    {
                        await _tradeService.CancelOrderAsync(transactionId);
                        Debug.WriteLine($"[MONITOR] Отменена заявка {transactionId} on {figi} @ {price}");
                        return false;
                    }
                }

                // если уровень не найден ни в Bids, ни в Asks — просто ждём пока появится
            }
            return false;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            Debug.WriteLine("MonitorOrderAsync cancelled by token");
            return false;
        }
    }

    //Рабочая слежка за целевой ценой с ПРОДАЖЕЙ позиции
    public async Task MonitorOrderMarketAsync(
    string figi,
    double price,
    int buyLots,
    bool isBuy,
    long minLots,
    long transactionId2,
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
                new OrderBookInstrument { Figi = figi, Depth = 20 }
            }
            }
        });

        var ticker = _settings.GetTickerByFigi(figi);
        var lastCheckTime = DateTime.MinValue; // Время последнего запроса
        bool isOrderActive = true; // Кешированный статус заявки

        try
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync(ct))
            {
                // 1) Сначала проверяем, остался ли ордер активным
                if ((DateTime.UtcNow - lastCheckTime).TotalSeconds >= 1.5)
                {
                    lastCheckTime = DateTime.UtcNow;
                    var activeOrders = await _tradeService.GetActiveOrdersAsync();
                    isOrderActive = activeOrders.Any(o => o.TransactionId == transactionId2);
                }
                if (!isOrderActive)
                {
                    Debug.WriteLine($"[MONITOR] Order {transactionId2} no longer active – stop monitoring");
                    return;
                }

                if (resp.PayloadCase != MarketDataResponse.PayloadOneofCase.Orderbook)
                    continue;

                var ob = resp.Orderbook;

                // ищем нужный уровень в Bids
                var bidLvl = ob.Bids.FirstOrDefault(lvl =>
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    return lvlPrice == price;
                });

                if (bidLvl != null)
                {
                    // если объём упал ниже минимума — выходим
                    if (bidLvl.Quantity < minLots)
                    {
                        await _tradeService.PlaceMarketOrderAsync(
                            securityCode: ticker,
                            quantity: buyLots,
                            isBuy: isBuy);
                        Debug.WriteLine($"[MONITOR] Закрыта позиция {transactionId2} on {figi} @ {price}");
                        await _tradeService.CancelOrderAsync(transactionId2); // отменяем заявку на фикс прибыли
                        return;
                    }
                    // иначе — уровень ещё держится, ждём следующий снимок
                    continue;
                }

                // если не нашли в Bids — проверяем Asks
                var askLvl = ob.Asks.FirstOrDefault(lvl =>
                {
                    var lvlPrice = lvl.Price.Units + lvl.Price.Nano / 1e9;
                    return lvlPrice == price;
                });

                if (askLvl != null)
                {
                    if (askLvl.Quantity < minLots)
                    {
                        await _tradeService.PlaceMarketOrderAsync(
                            securityCode: ticker,
                            quantity: buyLots,
                            isBuy: isBuy);
                        Debug.WriteLine($"[MONITOR] Закрыта позиция {transactionId2} on {figi} @ {price}");
                        await _tradeService.CancelOrderAsync(transactionId2);
                        return;
                    }
                }

                // если уровень не найден ни в Bids, ни в Asks — просто ждём пока появится
            }
            return;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            Debug.WriteLine("MonitorOrderAsync cancelled by token");
            return;
        }
    }
}