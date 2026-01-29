//FinamLiquidityProvider.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Trading.Core.Interfaces;
using Trading.Infrastructure.FinamV2.FinamGrpc;
using Grpc.Tradeapi.V1.Assets;
using Grpc.Tradeapi.V1.Marketdata;
using System.Globalization;

namespace Trading.Infrastructure.FinamV2.MarketData;

public sealed class FinamLiquidityProvider : ILiquidityProvider
{
    private readonly AssetsService.AssetsServiceClient _assets;
    private readonly MarketDataService.MarketDataServiceClient _md;
    private readonly JwtProvider _jwt;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ConcurrentDictionary<string, decimal> _lotSize = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _priceStep = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _dayVolume = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (DateTimeOffset startUtc, DateTimeOffset endUtc)[]> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public FinamLiquidityProvider(GrpcChannel channel, JwtProvider jwt)
    {
        _assets = new AssetsService.AssetsServiceClient(channel);
        _md = new MarketDataService.MarketDataServiceClient(channel);
        _jwt = jwt;
    }

    public Task StartAsync(string accountId, string[] symbols, Action<string> log, CancellationToken ct)
    {
        symbols = symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        _ = Task.Run(() => WarmUpAsync(accountId, symbols, log, ct), ct);
        return RunQuoteStreamAsync(symbols, log, ct);
    }

    private async Task WarmUpAsync(string accountId, string[] symbols, Action<string> log, CancellationToken ct)
    {
        foreach (var s0 in symbols)
        {
            var s = Normalize(s0);

            try { await LoadLotSizeAsync(accountId, s, log, ct); }
            catch (Exception ex) { log($"lot warmup fail {s}: {ex.Message}"); }

            try { await LoadScheduleAsync(s, log, ct); }
            catch (Exception ex) { log($"schedule warmup fail {s}: {ex.Message}"); }

            try { await SeedDayVolumeAsync(s, log, ct); }
            catch (Exception ex) { log($"volume seed fail {s}: {ex.Message}"); }
        }
    }

    private async Task SeedDayVolumeAsync(string symbol, Action<string> log, CancellationToken ct)
    {
        symbol = Normalize(symbol);

        await _jwt.EnsureJwtAsync(ct);

        // ВАЖНО: названия типов могут отличаться в твоей генерации.
        // Идея одна: запрос LastQuote по symbol, взять Volume.Value
        var resp = await _md.LastQuoteAsync(
            new QuoteRequest { Symbol = symbol },
            headers: _jwt.GetHeaders(),
            cancellationToken: ct);

        var v = ProtoDecimal.Parse(resp.Quote?.Volume?.Value);

        if (v is null)
        {
            log($"seed volume {symbol}: null");
            return;
        }

        _dayVolume[symbol] = v.Value;
        log($"seed volume {symbol}: {v.Value}");
    }

    private async Task LoadLotSizeAsync(string accountId, string symbol, Action<string> log, CancellationToken ct)
    {
        symbol = Normalize(symbol);
        try
        {
            await _jwt.EnsureJwtAsync(ct);

            var resp = await _assets.GetAssetAsync(
                new GetAssetRequest { AccountId = accountId, Symbol = symbol },
                headers: _jwt.GetHeaders(),
                cancellationToken: ct);

            // lot_size: google.type.Decimal { value: "..." }
            var lot = ProtoDecimal.Parse(resp.LotSize?.Value) ?? 1m;
            if (lot <= 0) lot = 1m;
            _lotSize[symbol] = lot;

            // min_step: long, decimals: int
            decimal step = 0m;

            try
            {
                var scale = Pow10(resp.Decimals);
                if (scale > 0)
                    step = (decimal)resp.MinStep / scale;
            }
            catch
            {
                step = 0m;
            }

            if (step > 0)
                _priceStep[symbol] = step;
            else
                log($"price_step not parsed for {symbol}: min_step={resp.MinStep} decimals={resp.Decimals}");

        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"asset meta load failed {symbol}: {ex.GetType().Name}: {ex.Message}");
            _lotSize[symbol] = 1m;
        }
    }

    private async Task LoadScheduleAsync(string symbol, Action<string> log, CancellationToken ct)
    {
        symbol = Normalize(symbol);

        try
        {
            await _jwt.EnsureJwtAsync(ct);

            var resp = await _assets.ScheduleAsync(
                new ScheduleRequest { Symbol = symbol },
                headers: _jwt.GetHeaders(),
                cancellationToken: ct);

            (DateTimeOffset startUtc, DateTimeOffset endUtc)[] intervals =
                resp.Sessions
                    .Select(x =>
                    {
                        var type = x.Type?.ToString()?.ToUpperInvariant() ?? "";
                        if (type.Contains("CLOSED"))
                            return (ok: false, start: default(DateTimeOffset), end: default(DateTimeOffset));

                        var st = x.Interval?.StartTime;
                        var en = x.Interval?.EndTime;
                        if (st == null || en == null)
                            return (ok: false, start: default(DateTimeOffset), end: default(DateTimeOffset));

                        var startUtc = DateTimeOffset.FromUnixTimeSeconds(st.Seconds).ToUniversalTime();
                        var endUtc = DateTimeOffset.FromUnixTimeSeconds(en.Seconds).ToUniversalTime();

                        return (ok: endUtc > startUtc, start: startUtc, end: endUtc);
                    })
                    .Where(x => x.ok)
                    .Select(x => (x.start, x.end))
                    .ToArray();

            // ФОЛЛБЭК: если расписание не пришло/пустое — ставим дефолт
            if (intervals.Length == 0)
            {
                // 00:00 UTC сегодня
                var d = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

                intervals = new[]
                {
                (startUtc: d.AddHours(7), endUtc: d.AddHours(15).AddMinutes(45)) // примерный MOEX дневной интервал в UTC
            };

                log($"schedule fallback used for {symbol}");
            }

            _sessions[symbol] = intervals;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"schedule load failed {symbol}: {ex.GetType().Name}: {ex.Message}");

            // даже при ошибке лучше положить fallback, иначе TryGet всегда false
            var d = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            _sessions[symbol] = new[]
            {
            (startUtc: d.AddHours(7), endUtc: d.AddHours(15).AddMinutes(45))
        };
        }
    }

    private async Task RunQuoteStreamAsync(string[] symbols, Action<string> log, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _jwt.EnsureJwtAsync(ct);

                using var call = _md.SubscribeQuote(
                    new SubscribeQuoteRequest { Symbols = { symbols } },
                    headers: _jwt.GetHeaders(),
                    cancellationToken: ct);

                await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
                {
                    foreach (var q in msg.Quote)
                    {
                        var v = ProtoDecimal.Parse(q.Volume?.Value);
                        if (v is null) continue;

                        var key = Normalize(q.Symbol);
                        _dayVolume[key] = v.Value;
                    }
                }
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            {
                log($"quote rpc error: {ex.StatusCode} {ex.Status.Detail}");
                await Task.Delay(300, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log($"quote error: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(300, ct);
            }
        }
    }

    private static string Normalize(string s)
    {
        s = (s ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) return s;

        // если вдруг приходит "SBER", а везде ты живёшь "SBER@MISX"
        if (!s.Contains('@'))
            s = s + "@MISX";

        return s;
    }

    private static decimal Pow10(int decimals)
    {
        decimal p = 1m;
        for (int i = 0; i < decimals; i++) p *= 10m;
        return p;
    }

    public bool TryGet(string symbol, DateTimeOffset nowUtc, int windowMinutes, out LiquiditySnapshot snap)
    {
        snap = default;

        var key = Normalize(symbol);

        var okLot = _lotSize.TryGetValue(key, out var lot);
        var okVol = _dayVolume.TryGetValue(key, out var dayVol);
        var okSes = _sessions.TryGetValue(key, out var sessions);

        if (!okLot || !okVol || !okSes)
            return false;

        _priceStep.TryGetValue(key, out var step); // может быть 0 — это ок, скальпер сможет вывести из стакана

        var elapsed = ComputeElapsedMinutes(sessions, nowUtc);
        if (elapsed <= 0.0) return false;

        var w = Math.Max(1, windowMinutes);
        var avg = dayVol * w / (decimal)elapsed;

        snap = new LiquiditySnapshot(
            LotSize: lot,
            DayVolumeShares: dayVol,
            PriceStep: step,
            ElapsedTradingMinutes: elapsed,
            AvgWindowVolumeShares: avg);

        return true;
    }

    private static double ComputeElapsedMinutes((DateTimeOffset startUtc, DateTimeOffset endUtc)[] sessions, DateTimeOffset nowUtc)
    {
        double sum = 0;
        foreach (var (st, en) in sessions)
        {
            if (nowUtc <= st) continue;
            var till = nowUtc < en ? nowUtc : en;
            if (till > st) sum += (till - st).TotalMinutes;
        }
        return sum;
    }
}
