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

        var candles = await LoadCandlesAsync(symbolRow.Code, req.Interval, req.From, req.To, cancellationToken);
        if (candles.Count < strategy.WarmupBars + 5)
            throw new InvalidOperationException($"not_enough_candles: got {candles.Count}, need >= {strategy.WarmupBars + 5}");

        logger.LogInformation("Backtest {Id}: {Bars} bars, strategy={Kind}, capital={Cap}",
            req.BacktestId, candles.Count, strategyRow.Kind, req.InitialCapital);

        var cash = req.InitialCapital;
        decimal positionQty = 0;
        decimal positionEntry = 0;
        var equityCurve = new List<EquityPoint>(candles.Count);
        var trades = new List<TradeResult>();
        var commissionRate = req.CommissionPercent / 100m;

        for (var i = strategy.WarmupBars; i < candles.Count; i++)
        {
            var candle = candles[i];
            var history = candles.Take(i + 1).ToList();
            var ctx = new BacktestStrategyContext(
                symbolRow.Code, candle.CloseTime, history,
                positionQty > 0 ? positionQty : null,
                positionQty > 0 ? positionEntry : null,
                cash);

            var decision = strategy.OnCandle(candle, ctx);
            if (decision is not null && decision.Side is { } side)
            {
                var price = decision.Price ?? candle.Close;
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

            var equity = cash + positionQty * candle.Close;
            equityCurve.Add(new EquityPoint(candle.CloseTime, equity));
        }

        if (positionQty > 0)
        {
            var last = candles[^1];
            var proceeds = positionQty * last.Close;
            var commission = proceeds * commissionRate;
            var pnl = (last.Close - positionEntry) * positionQty - commission;
            cash += proceeds - commission;
            trades.Add(new TradeResult(last.CloseTime, "Sell(eod)", last.Close, positionQty, pnl));
            equityCurve[^1] = new EquityPoint(last.CloseTime, cash);
        }

        return ComputeMetrics(req.InitialCapital, equityCurve, trades);
    }

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

        var closingTrades = trades.Where(t => t.Action.StartsWith("Sell", StringComparison.OrdinalIgnoreCase)).ToList();
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
