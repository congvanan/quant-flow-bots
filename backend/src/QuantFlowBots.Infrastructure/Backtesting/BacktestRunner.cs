using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Backtesting;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Strategies;

namespace QuantFlowBots.Infrastructure.Backtesting;

public sealed class BacktestRunner(
    QuantFlowBotsDbContext db,
    IExchangeClient exchange,
    IStrategyFactory strategyFactory,
    IHttpClientFactory httpFactory,
    ILogger<BacktestRunner> logger) : IBacktestRunner
{
    public async Task<BacktestMetrics> RunAsync(BacktestRequest req, CancellationToken cancellationToken)
    {
        var strategyRow = await db.Strategies.FirstOrDefaultAsync(s => s.Id == req.StrategyId, cancellationToken)
            ?? throw new InvalidOperationException("strategy_not_found");
        var symbolRow = await db.Symbols.FirstOrDefaultAsync(s => s.Id == req.SymbolId, cancellationToken)
            ?? throw new InvalidOperationException("symbol_not_found");

        var strategy = strategyFactory.Create(strategyRow.Kind);
        var paramJson = string.IsNullOrWhiteSpace(req.ParametersJson) ? strategyRow.ParametersJson : req.ParametersJson;
        strategy.Configure(StrategyBase.ParseJson(paramJson));

        // Futures mode: nến từ fapi (giá futures lệch spot theo basis), long+short, margin model.
        var isFutures = req.Market == MarketKind.Futures;
        var leverage = isFutures ? Math.Clamp(req.Leverage, 1m, 125m) : 1m;

        var candles = isFutures
            ? await LoadFuturesCandlesAsync(symbolRow.Code, req.Interval, req.From, req.To, cancellationToken)
            : await LoadCandlesAsync(symbolRow.Code, req.Interval, req.From, req.To, cancellationToken);
        if (candles.Count < strategy.WarmupBars + 5)
            throw new InvalidOperationException($"not_enough_candles: got {candles.Count}, need >= {strategy.WarmupBars + 5}");

        logger.LogInformation("Backtest {Id}: {Bars} bars, strategy={Kind}, capital={Cap}, market={Market}, lev={Lev}",
            req.BacktestId, candles.Count, strategyRow.Kind, req.InitialCapital, req.Market, leverage);

        var cash = req.InitialCapital;
        decimal positionQty = 0;   // signed: > 0 long, < 0 short (short chỉ trong futures mode)
        decimal positionEntry = 0;
        var equityCurve = new List<EquityPoint>(candles.Count);
        var trades = new List<TradeResult>();
        var commissionRate = req.CommissionPercent / 100m;

        // Tránh O(N²): trước đây mỗi vòng `candles.Take(i+1).ToList()` copy avg N/2 items →
        // tổng ~N²/2 copies + N allocations. Với 15K nến (5 tháng 15m) = ~112M item copies → 7-8s.
        // PrefixList là wrapper read-only — chỉ giữ reference + length, O(1) constant per iteration.
        for (var i = strategy.WarmupBars; i < candles.Count; i++)
        {
            var candle = candles[i];
            var history = new PrefixList<CandleData>(candles, i + 1);
            // Truyền positionQty CÓ DẤU (âm = short) để strategy quản lý exit cho cả 2 chiều.
            var ctx = new BacktestStrategyContext(
                symbolRow.Code, candle.CloseTime, history,
                positionQty != 0 ? positionQty : null,
                positionQty != 0 ? positionEntry : null,
                cash);

            var decision = strategy.OnCandle(candle, ctx);
            if (decision is not null && decision.Side is { } side)
            {
                var price = decision.Price ?? candle.Close;
                if (isFutures)
                {
                    // Margin model: mở lệnh chỉ trừ commission, notional = cash × leverage.
                    // Đóng lệnh cộng PnL − commission. Equity = cash + unrealized.
                    if (side == OrderSide.Buy)
                    {
                        if (positionQty < 0)
                        {
                            // Cover short
                            var qtyAbs = -positionQty;
                            var commission = qtyAbs * price * commissionRate;
                            var pnl = (positionEntry - price) * qtyAbs - commission;
                            cash += pnl;
                            trades.Add(new TradeResult(candle.CloseTime, "Cover", price, qtyAbs, pnl));
                            positionQty = 0; positionEntry = 0;
                        }
                        else if (positionQty == 0 && cash > 0)
                        {
                            // Open long
                            var qty = cash * leverage / price;
                            var commission = qty * price * commissionRate;
                            cash -= commission;
                            positionQty = qty; positionEntry = price;
                            trades.Add(new TradeResult(candle.CloseTime, "Buy", price, qty, -commission));
                        }
                    }
                    else // Sell
                    {
                        if (positionQty > 0)
                        {
                            // Close long
                            var commission = positionQty * price * commissionRate;
                            var pnl = (price - positionEntry) * positionQty - commission;
                            cash += pnl;
                            trades.Add(new TradeResult(candle.CloseTime, "Sell", price, positionQty, pnl));
                            positionQty = 0; positionEntry = 0;
                        }
                        else if (positionQty == 0 && cash > 0)
                        {
                            // Open short
                            var qty = cash * leverage / price;
                            var commission = qty * price * commissionRate;
                            cash -= commission;
                            positionQty = -qty; positionEntry = price;
                            trades.Add(new TradeResult(candle.CloseTime, "Short", price, qty, -commission));
                        }
                    }
                }
                else
                {
                    // Spot: long-only cash model (behavior cũ giữ nguyên).
                    if (side == OrderSide.Buy && positionQty == 0 && cash > 0)
                    {
                        var qty = cash / price;
                        var commission = qty * price * commissionRate;
                        positionQty = qty;
                        positionEntry = price;
                        cash -= qty * price + commission;
                        trades.Add(new TradeResult(candle.CloseTime, "Buy", price, qty, -commission));
                    }
                    else if (side == OrderSide.Sell && positionQty > 0)
                    {
                        var proceeds = positionQty * price;
                        var commission = proceeds * commissionRate;
                        var pnl = (price - positionEntry) * positionQty - commission;
                        cash += proceeds - commission;
                        trades.Add(new TradeResult(candle.CloseTime, "Sell", price, positionQty, pnl));
                        positionQty = 0;
                        positionEntry = 0;
                    }
                }
            }

            var equity = isFutures
                ? cash + UnrealizedPnl(positionQty, positionEntry, candle.Close)
                : cash + positionQty * candle.Close;
            equityCurve.Add(new EquityPoint(candle.CloseTime, equity));

            // Liquidation xấp xỉ (futures): equity về 0 → force close, dừng nếu cháy tài khoản.
            // Mô hình đơn giản hoá — Binance thực tế liquidate sớm hơn (maintenance margin ~0.4-2%),
            // nên backtest này là LẠC QUAN hơn thực tế một chút với leverage cao.
            if (isFutures && equity <= 0m && positionQty != 0m)
            {
                var qtyAbs = Math.Abs(positionQty);
                var pnl = UnrealizedPnl(positionQty, positionEntry, candle.Close);
                cash += pnl;
                trades.Add(new TradeResult(candle.CloseTime, positionQty > 0 ? "Sell(liq)" : "Cover(liq)", candle.Close, qtyAbs, pnl));
                positionQty = 0; positionEntry = 0;
                equityCurve[^1] = new EquityPoint(candle.CloseTime, Math.Max(cash, 0m));
                if (cash <= 0m)
                {
                    logger.LogWarning("Backtest {Id}: account liquidated at {At} — stopping", req.BacktestId, candle.CloseTime);
                    break;
                }
            }
        }

        if (positionQty != 0)
        {
            var last = candles[^1];
            if (isFutures)
            {
                var qtyAbs = Math.Abs(positionQty);
                var commission = qtyAbs * last.Close * commissionRate;
                var pnl = UnrealizedPnl(positionQty, positionEntry, last.Close) - commission;
                cash += pnl;
                trades.Add(new TradeResult(last.CloseTime, positionQty > 0 ? "Sell(eod)" : "Cover(eod)", last.Close, qtyAbs, pnl));
            }
            else
            {
                var proceeds = positionQty * last.Close;
                var commission = proceeds * commissionRate;
                var pnl = (last.Close - positionEntry) * positionQty - commission;
                cash += proceeds - commission;
                trades.Add(new TradeResult(last.CloseTime, "Sell(eod)", last.Close, positionQty, pnl));
            }
            equityCurve[^1] = new EquityPoint(last.CloseTime, cash);
        }

        return ComputeMetrics(req.InitialCapital, equityCurve, trades);
    }

    private static decimal UnrealizedPnl(decimal positionQty, decimal entry, decimal mark) => positionQty switch
    {
        > 0 => (mark - entry) * positionQty,
        < 0 => (entry - mark) * (-positionQty),
        _ => 0m,
    };

    private async Task<IReadOnlyList<CandleData>> LoadCandlesAsync(
        string symbol, CandleInterval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<CandleData>();
        var cursor = from;
        while (cursor < to)
        {
            var batch = await exchange.GetCandlesAsync(symbol, interval, cursor, to, 1000, cancellationToken);
            if (batch.Count == 0) break;
            all.AddRange(batch);
            var lastOpen = batch[^1].OpenTime;
            if (lastOpen <= cursor) break;
            cursor = lastOpen.AddSeconds((int)interval);
            if (batch.Count < 1000) break;
        }
        return all;
    }

    /// <summary>
    /// Nến Futures USDT-M từ fapi production (public, không cần key). Response format giống hệt
    /// spot klines. Dùng client "alpha-public" — không qua BinanceGate vì traffic này không tính
    /// vào spot weight (fapi limit riêng 2400/min, kline weight 2/call → 15 calls = 30 weight).
    /// </summary>
    private async Task<IReadOnlyList<CandleData>> LoadFuturesCandlesAsync(
        string symbol, CandleInterval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("alpha-public");
        http.Timeout = TimeSpan.FromSeconds(15);
        var iv = ToBinanceInterval(interval);

        var all = new List<CandleData>();
        var cursor = from;
        while (cursor < to)
        {
            var url = $"https://fapi.binance.com/fapi/v1/klines?symbol={symbol.ToUpperInvariant()}&interval={iv}" +
                      $"&startTime={cursor.ToUnixTimeMilliseconds()}&endTime={to.ToUnixTimeMilliseconds()}&limit=1000";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) break;

            var count = 0;
            DateTimeOffset lastOpen = cursor;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != System.Text.Json.JsonValueKind.Array || row.GetArrayLength() < 9) continue;
                var openTime = DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64());
                var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64());
                all.Add(new CandleData(
                    symbol, interval, openTime, closeTime,
                    ParseDec(row[1]), ParseDec(row[2]), ParseDec(row[3]), ParseDec(row[4]),
                    ParseDec(row[5]), ParseDec(row[7]), row[8].GetInt32(), IsClosed: true));
                lastOpen = openTime;
                count++;
            }
            if (count == 0 || lastOpen <= cursor) break;
            cursor = lastOpen.AddSeconds((int)interval);
            if (count < 1000) break;
        }
        return all;

        static decimal ParseDec(System.Text.Json.JsonElement el) =>
            decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static string ToBinanceInterval(CandleInterval interval) => interval switch
    {
        CandleInterval.OneMinute => "1m",
        CandleInterval.FiveMinutes => "5m",
        CandleInterval.FifteenMinutes => "15m",
        CandleInterval.ThirtyMinutes => "30m",
        CandleInterval.OneHour => "1h",
        CandleInterval.TwoHours => "2h",
        CandleInterval.FourHours => "4h",
        CandleInterval.OneDay => "1d",
        _ => "15m",
    };

    private static BacktestMetrics ComputeMetrics(decimal initialCapital, IReadOnlyList<EquityPoint> curve, IReadOnlyList<TradeResult> trades)
    {
        if (curve.Count == 0)
            return new BacktestMetrics(initialCapital, 0, 0, 0, 0, 0, 0, curve);

        var finalEquity = curve[^1].Equity;
        var totalReturnPct = (finalEquity - initialCapital) / initialCapital * 100m;

        decimal peak = curve[0].Equity;
        decimal maxDd = 0m;
        foreach (var p in curve)
        {
            if (p.Equity > peak) peak = p.Equity;
            var dd = peak == 0 ? 0 : (peak - p.Equity) / peak * 100m;
            if (dd > maxDd) maxDd = dd;
        }

        decimal sharpe = 0m;
        if (curve.Count > 1)
        {
            var returns = new double[curve.Count - 1];
            for (var i = 1; i < curve.Count; i++)
            {
                var prev = (double)curve[i - 1].Equity;
                returns[i - 1] = prev == 0 ? 0 : ((double)curve[i].Equity - prev) / prev;
            }
            var mean = returns.Average();
            var variance = returns.Select(r => (r - mean) * (r - mean)).Sum() / Math.Max(1, returns.Length - 1);
            var std = Math.Sqrt(variance);
            // assume 1-minute bars; ~525600 bars/year
            const double barsPerYear = 525_600.0;
            sharpe = std == 0 ? 0m : (decimal)(mean / std * Math.Sqrt(barsPerYear));
        }

        // Closing legs: Sell* (đóng long) + Cover* (đóng short — futures mode).
        var closingTrades = trades.Where(t =>
            t.Action.StartsWith("Sell", StringComparison.OrdinalIgnoreCase) ||
            t.Action.StartsWith("Cover", StringComparison.OrdinalIgnoreCase)).ToList();
        var winCount = closingTrades.Count(t => t.RealizedPnl > 0);
        var winRate = closingTrades.Count == 0 ? 0m : (decimal)winCount / closingTrades.Count * 100m;

        return new BacktestMetrics(
            FinalEquity: finalEquity,
            TotalReturnPercent: totalReturnPct,
            MaxDrawdownPercent: maxDd,
            SharpeRatio: sharpe,
            TradeCount: closingTrades.Count,
            WinCount: winCount,
            WinRatePercent: winRate,
            EquityCurve: curve);
    }
}

public sealed record TradeResult(DateTimeOffset At, string Action, decimal Price, decimal Quantity, decimal RealizedPnl);

/// <summary>
/// Read-only prefix view over an IReadOnlyList — exposes chỉ N item đầu mà không copy data.
/// Dùng trong BacktestRunner để tránh O(N²) khi pass history mỗi nến cho strategy.
/// Cost: 1 reference + 1 int per iteration thay vì List allocation + N item copy.
/// </summary>
internal sealed class PrefixList<T>(IReadOnlyList<T> source, int count) : IReadOnlyList<T>
{
    public T this[int index] => index >= 0 && index < count
        ? source[index]
        : throw new ArgumentOutOfRangeException(nameof(index));
    public int Count => count;
    public System.Collections.Generic.IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < count; i++) yield return source[i];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class BacktestStrategyContext(
    string symbol,
    DateTimeOffset now,
    IReadOnlyList<CandleData> history,
    decimal? openQty,
    decimal? entryPrice,
    decimal cash) : IStrategyContext
{
    public string Symbol => symbol;
    public DateTimeOffset Now => now;
    public IReadOnlyList<CandleData> History => history;
    public decimal? OpenPositionQuantity => openQty;
    public decimal? OpenPositionEntryPrice => entryPrice;
    public decimal Cash => cash;
}
