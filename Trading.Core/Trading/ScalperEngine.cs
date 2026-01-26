//ScalperEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Trading.Core.Interfaces;
using Trading.Core.Models;
using Trading.Core.Trading;

namespace Trading.Core.Trading;

public sealed class ScalperEngine : IDisposable
{
    private readonly IMarketDataFeed _md;
    private readonly ITradingGateway _trading;
    private readonly Action<string> _log;

    private readonly OrderAwaiter _awaiter = new();

    private readonly ConcurrentDictionary<string, OrderBookSnapshot> _lastSnap = new();
    private readonly ConcurrentDictionary<string, SymbolSession> _sessions = new();

    private CancellationTokenSource? _cts;
    private ScalperSettings _settings = new();
    private string _accountId = "";

    public bool IsRunning => _cts != null;

    public ScalperEngine(IMarketDataFeed md, ITradingGateway trading, Action<string> log)
    {
        _md = md;
        _trading = trading;
        _log = log;

        // События — это твой "источник истины". Поллинга тут быть не должно.
        _md.OrderBook += OnOrderBook;
        _trading.OrderUpdated += _awaiter.OnOrderUpdate;
    }

    public void Start(string accountId, ScalperSettings settings)
    {
        if (_cts != null) return;

        _accountId = accountId;
        _settings = settings;

        _cts = new CancellationTokenSource();
        _log($"strategy started, qty={settings.Qty}, minDensity={settings.DensityMinSize}");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        _log("strategy stopped");
    }

    public async Task ManualTestAsync(string symbol, Side side)
    {
        // Ручной smoke: запускает один цикл сделки без детектора плотностей
        if (_cts == null)
            throw new InvalidOperationException("strategy is not running");

        await RunCycleAsync(symbol, new DensitySignal(symbol, side, 0m, 0m), manual: true, _cts.Token);
    }

    public async Task PanicAsync()
    {
        // Panic: стопаем стратегию и отменяем то, что она знает
        var cts = _cts;
        Stop();

        foreach (var kv in _sessions)
        {
            var s = kv.Value;

            // Отменяем известные заявки (если есть)
            if (!string.IsNullOrWhiteSpace(s.EntryOrderId))
            {
                try { await _trading.CancelAsync(_accountId, s.EntryOrderId!, CancellationToken.None); }
                catch { /* тут не геройствуем */ }
            }

            if (!string.IsNullOrWhiteSpace(s.TpOrderId))
            {
                try { await _trading.CancelAsync(_accountId, s.TpOrderId!, CancellationToken.None); }
                catch { }
            }
        }

        _log("panic done");
    }

    private void OnOrderBook(OrderBookSnapshot snap)
    {
        _lastSnap[snap.Symbol] = snap;

        // Если стратегия не запущена — просто храним снапшоты (для UI/ручного теста)
        var cts = _cts;
        if (cts == null) return;

        var session = _sessions.GetOrAdd(snap.Symbol, s => new SymbolSession(s));

        // Защита от параллельного запуска цикла по одному символу
        if (!session.TryEnterCooldown(_settings.CooldownMs))
            return;

        // Детектор плотностей
        if (!DensityDetector.TryFind(snap, _settings, out var signal))
            return;

        // Запускаем сделку в фоне. OnOrderBook должен быть быстрым.
        _ = Observe(RunCycleAsync(snap.Symbol, signal, manual: false, cts.Token),
            $"cycle faulted {snap.Symbol}");
    }

    private async Task RunCycleAsync(string symbol, DensitySignal signal, bool manual, CancellationToken ct)
    {
        var session = _sessions.GetOrAdd(symbol, s => new SymbolSession(s));

        // Лочим символ на время сделки
        if (!await session.Lock.WaitAsync(0, ct))
            return;

        try
        {
            // Берём актуальный снапшот
            if (!_lastSnap.TryGetValue(symbol, out var snap))
                return;

            // В ручном тесте мы не используем плотность — берём best bid/ask и делаем "вход"
            if (manual)
            {
                var bb = snap.Bids.FirstOrDefault();
                var ba = snap.Asks.FirstOrDefault();
                if (bb == null || ba == null) return;

                // Псевдо-сигнал для ручного теста: цена входа чуть в молоко
                var entrySide = signal.Side;
                var entryPrice = entrySide == Side.Buy ? bb.Price * 0.98m : ba.Price * 1.02m;

                await RunTradeLifecycle(symbol, entrySide, entryPrice, ct);
                return;
            }

            // Авто-режим: входим на цене плотности
            session.ClusterPrice = signal.Price;
            session.ClusterSize = signal.Size;
            session.ClusterSide = signal.Side;

            _log($"{symbol} density {signal.Side} p={signal.Price} size={signal.Size}");

            await RunTradeLifecycle(symbol, signal.Side, signal.Price, ct);
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private async Task RunTradeLifecycle(string symbol, Side entrySide, decimal entryPrice, CancellationToken ct)
    {
        var session = _sessions.GetOrAdd(symbol, s => new SymbolSession(s));
        var qty = _settings.Qty;

        // 1) Выставляем entry лимитку
        var entryClientId = $"bot-entry-{symbol}-{Guid.NewGuid():N}";
        var entryOrderId = await _trading.PlaceLimitAsync(_accountId, symbol, entrySide, entryPrice, qty, entryClientId, ct);
        session.EntryOrderId = entryOrderId;

        _log($"{symbol} entry placed {entrySide} p={entryPrice} id={entryOrderId}");

        // 2) Ждём финальный статус entry (без поллинга, только через стрим)
        var entryFinal = await _awaiter.WaitFinalAsync(entryOrderId, ct);

        _log($"{symbol} entry final {entryFinal.Status} id={entryOrderId}");

        if (entryFinal.Status != OrderStatus.Filled)
        {
            // Не вошли — нечего сопровождать
            return;
        }

        // 3) Ставим тейк
        var tpSide = entrySide == Side.Buy ? Side.Sell : Side.Buy;
        var tpPrice = entrySide == Side.Buy
            ? entryPrice * (1m + _settings.TakeProfitPct)
            : entryPrice * (1m - _settings.TakeProfitPct);

        var tpClientId = $"bot-tp-{symbol}-{Guid.NewGuid():N}";
        var tpOrderId = await _trading.PlaceLimitAsync(_accountId, symbol, tpSide, tpPrice, qty, tpClientId, ct);
        session.TpOrderId = tpOrderId;

        _log($"{symbol} tp placed {tpSide} p={tpPrice} id={tpOrderId}");

        // 4) Одновременно ждём: либо тейк исполнился, либо плотность развалилась
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tpFinalTask = _awaiter.WaitFinalAsync(tpOrderId, ct);
        var breakTask = WaitDensityBreakAsync(symbol, linked.Token);

        var done = await Task.WhenAny(tpFinalTask, breakTask);

        if (done == tpFinalTask)
        {
            // Тейк завершился — отменяем мониторинг развала
            linked.Cancel();

            var tpFinal = await tpFinalTask;
            _log($"{symbol} tp final {tpFinal.Status} id={tpOrderId}");
            return;
        }

        // 5) Развал плотности — отменяем тейк и выходим маркетом
        _log($"{symbol} density broken -> emergency exit");

        try
        {
            await _trading.CancelAsync(_accountId, tpOrderId, CancellationToken.None);
        }
        catch
        {
            // Если не успели отменить — бывает, дальше всё равно пробуем закрыться
        }

        await _trading.PlaceMarketAsync(_accountId, symbol, tpSide, qty, $"bot-exit-{symbol}-{Guid.NewGuid():N}", ct);
        _log($"{symbol} market exit sent {tpSide} qty={qty}");
    }

    private async Task WaitDensityBreakAsync(string symbol, CancellationToken ct)
    {
        // Периодически проверяем, что плотность на ClusterPrice не развалилась
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.BreakCheckMs));

        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!_sessions.TryGetValue(symbol, out var session))
                continue;

            // Если нет данных о плотности — нечего проверять
            if (session.ClusterPrice is null || session.ClusterSize is null || session.ClusterSide is null)
                continue;

            if (!_lastSnap.TryGetValue(symbol, out var snap))
                continue;

            if (IsDensityBroken(snap, session.ClusterSide.Value, session.ClusterPrice.Value, session.ClusterSize.Value))
                return;
        }
    }

    private bool IsDensityBroken(OrderBookSnapshot snap, Side side, decimal price, decimal originalSize)
    {
        var levels = side == Side.Buy ? snap.Bids : snap.Asks;

        // Ищем уровень по цене. Если исчез — считаем что развалился.
        var lvl = levels.FirstOrDefault(l => l.Price == price);
        if (lvl == null) return true;

        // Если объём упал ниже порога — развал
        return lvl.Size < originalSize * _settings.BreakFactor;
    }

    private Task Observe(Task t, string tag)
    {
        // Чтобы исключения из фоновых задач не терялись
        return t.ContinueWith(x =>
        {
            if (x.IsFaulted)
                _log($"{tag}: {x.Exception}");
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        Stop();
        _md.OrderBook -= OnOrderBook;
        _trading.OrderUpdated -= _awaiter.OnOrderUpdate;
    }

    private sealed class SymbolSession
    {
        public string Symbol { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public string? EntryOrderId;
        public string? TpOrderId;

        public Side? ClusterSide;
        public decimal? ClusterPrice;
        public decimal? ClusterSize;

        private long _nextAllowedTicks;

        public SymbolSession(string symbol) => Symbol = symbol;

        public bool TryEnterCooldown(int cooldownMs)
        {
            var now = DateTimeOffset.UtcNow.Ticks;
            var next = Interlocked.Read(ref _nextAllowedTicks);

            if (now < next)
                return false;

            // резервируем "окно" следующего допуска
            Interlocked.Exchange(ref _nextAllowedTicks, now + TimeSpan.FromMilliseconds(cooldownMs).Ticks);
            return true;
        }
    }
}
