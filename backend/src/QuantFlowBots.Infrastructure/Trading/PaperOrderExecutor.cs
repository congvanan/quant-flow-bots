using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Strategies;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed class PaperOrderExecutor(
    QuantFlowBotsDbContext db,
    PositionMonitor monitor,
    ILogger<PaperOrderExecutor> logger) : IPaperOrderExecutor
{
    private const decimal CommissionPercent = 0.1m;

    public async Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest req, CancellationToken cancellationToken)
    {
        // Validate against exchangeInfo filters (minQty / stepSize / tickSize / minNotional).
        // Auto-close paths from PositionMonitor should bypass strict validation (always allow exits).
        var isAutoClose = req.Reason.StartsWith("auto:", StringComparison.OrdinalIgnoreCase);
        var symbol = await db.Symbols.FirstOrDefaultAsync(s => s.Id == req.SymbolId, cancellationToken);
        decimal qty = req.Quantity;
        decimal price = req.Price;
        if (symbol is not null && !isAutoClose)
        {
            var v = OrderValidator.Validate(symbol, qty, price);
            if (!v.Approved)
            {
                logger.LogWarning("PaperOrderExecutor rejected {Side} {Symbol}: {Reason}", req.Side, symbol.Code, v.Reason);
                var rejected = new Order
                {
                    BotId = req.BotId,
                    BotRunId = req.BotRunId,
                    ApiKeyId = req.ApiKeyId,
                    SymbolId = req.SymbolId,
                    Mode = TradingMode.Paper,
                    Side = req.Side,
                    Type = OrderType.Market,
                    Status = OrderStatus.Rejected,
                    Price = price,
                    Quantity = qty,
                    FilledQuantity = 0,
                    AveragePrice = 0,
                    Commission = 0,
                    ClientOrderId = $"paper_{Guid.NewGuid():N}",
                    RejectReason = v.Reason,
                };
                db.Orders.Add(rejected);
                await db.SaveChangesAsync(cancellationToken);
                return new PaperOrderResult(rejected.Id, OrderStatus.Rejected, 0, 0, 0, null);
            }
            qty = v.AdjustedQuantity;
            price = v.AdjustedPrice;
        }

        var commission = price * qty * (CommissionPercent / 100m);
        var order = new Order
        {
            BotId = req.BotId,
            BotRunId = req.BotRunId,
            ApiKeyId = req.ApiKeyId,
            SymbolId = req.SymbolId,
            Mode = TradingMode.Paper,
            Side = req.Side,
            Type = OrderType.Market,
            Status = OrderStatus.Filled,
            Price = price,
            Quantity = qty,
            FilledQuantity = qty,
            AveragePrice = price,
            Commission = commission,
            ClientOrderId = $"paper_{Guid.NewGuid():N}",
            FilledAt = DateTimeOffset.UtcNow,
        };
        db.Orders.Add(order);

        // Scope theo account (multi-account) + theo PositionId nếu close targeted. Single-account
        // (req.ApiKeyId == null) khớp mọi vị thế của bot như cũ.
        var open = await db.Positions
            .Where(p => p.BotId == req.BotId && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open
                && (req.ApiKeyId == null || p.ApiKeyId == req.ApiKeyId)
                && (req.PositionId == null || p.Id == req.PositionId))
            .FirstOrDefaultAsync(cancellationToken);

        decimal realizedPnl = 0;
        Position? touched = null;

        if (req.Side == OrderSide.Buy)
        {
            var bot = await db.Bots.Include(b => b.Symbol).FirstOrDefaultAsync(b => b.Id == req.BotId, cancellationToken);
            if (open is null)
            {
                var stopLossPrice = await ComputeStopLossAsync(bot, req.SymbolId, price, cancellationToken);
                var (legacyTp, tpLevels) = BuildTpLevels(bot, price, qty);

                var pos = new Position
                {
                    BotId = req.BotId,
                    BotRunId = req.BotRunId,
                    ApiKeyId = req.ApiKeyId,
                    SymbolId = req.SymbolId,
                    Mode = TradingMode.Paper,
                    Side = PositionSide.Long,
                    Status = PositionStatus.Open,
                    Quantity = qty,
                    OriginalQuantity = qty,
                    EntryPrice = price,
                    HighestPriceSinceEntry = price,
                    StopLossPrice = stopLossPrice,
                    TakeProfitPrice = legacyTp,
                    TakeProfitLevelsJson = tpLevels.Count > 0 ? TpLevelsCodec.Serialize(tpLevels) : null,
                    TrailingStopPercent = bot?.DefaultTrailingStopPercent,
                };
                db.Positions.Add(pos);
                touched = pos;
            }
            else
            {
                // Add to existing position (rare for our risk engine; kept for safety).
                var newQty = open.Quantity + qty;
                open.EntryPrice = (open.EntryPrice * open.Quantity + price * qty) / newQty;
                open.Quantity = newQty;
                open.OriginalQuantity += qty;
                if (bot is not null)
                {
                    open.StopLossPrice = await ComputeStopLossAsync(bot, req.SymbolId, open.EntryPrice, cancellationToken);
                    var (legacyTp, tpLevels) = BuildTpLevels(bot, open.EntryPrice, open.OriginalQuantity);
                    open.TakeProfitPrice = legacyTp;
                    open.TakeProfitLevelsJson = tpLevels.Count > 0 ? TpLevelsCodec.Serialize(tpLevels) : null;
                    open.BreakEvenTriggered = false;
                }
                touched = open;
            }
        }
        else
        {
            if (open is null)
            {
                logger.LogWarning("Sell signal without open long for bot {BotId}, skipping.", req.BotId);
                order.Status = OrderStatus.Rejected;
                order.RejectReason = "no_open_position";
                await db.SaveChangesAsync(cancellationToken);
                return new PaperOrderResult(order.Id, OrderStatus.Rejected, 0, 0, 0, null);
            }
            var closeQty = Math.Min(open.Quantity, qty);
            realizedPnl = (price - open.EntryPrice) * closeQty - commission;
            open.Quantity -= closeQty;
            open.RealizedPnl += realizedPnl;
            if (open.Quantity <= 0)
            {
                open.Status = PositionStatus.Closed;
                open.ExitPrice = price;
                open.ClosedAt = DateTimeOffset.UtcNow;
            }
            touched = open;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (touched is not null)
        {
            // Only re-register with monitor when this is a freshly opened position
            // (Buy when no open existed) or a full close. Partial closes from the
            // monitor itself update state in-place; re-adding here would wipe TP
            // level + break-even progress.
            if (touched.Status == PositionStatus.Closed)
            {
                await monitor.RemoveAsync(touched.Id);
            }
            else if (req.Side == OrderSide.Buy && open is null)
            {
                var sym = symbol ?? await db.Symbols.FirstOrDefaultAsync(s => s.Id == touched.SymbolId, cancellationToken);
                var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == touched.BotId, cancellationToken);
                if (sym is not null)
                {
                    var mp = new MonitoredPosition(
                        touched.Id, touched.BotId, touched.SymbolId, sym.Code, touched.Side,
                        touched.Quantity, touched.OriginalQuantity,
                        touched.EntryPrice, touched.StopLossPrice, touched.TakeProfitPrice,
                        touched.TrailingStopPercent, touched.HighestPriceSinceEntry ?? touched.EntryPrice)
                    {
                        TpLevels = TpLevelsCodec.Parse(touched.TakeProfitLevelsJson),
                        BreakEvenEnabled = bot?.BreakEvenEnabled ?? false,
                        BreakEvenTriggerPercent = bot?.BreakEvenTriggerPercent,
                        BreakEvenOffsetPercent = bot?.BreakEvenOffsetPercent ?? 0.1m,
                        BreakEvenTriggered = touched.BreakEvenTriggered,
                    };
                    await monitor.AddAsync(mp);
                }
            }
        }

        return new PaperOrderResult(order.Id, OrderStatus.Filled, qty, price, realizedPnl, touched?.Id.ToString());
    }

    private async Task<decimal?> ComputeStopLossAsync(Bot? bot, int symbolId, decimal entryPrice, CancellationToken cancellationToken)
    {
        if (bot is null) return null;

        if (bot.StopLossKind == StopLossKind.Atr && bot.AtrPeriod > 0 && bot.AtrMultiplier > 0)
        {
            var atr = await ComputeAtrAsync(symbolId, bot.AtrPeriod, cancellationToken);
            if (atr.HasValue && atr.Value > 0)
            {
                return entryPrice - atr.Value * bot.AtrMultiplier;
            }
            logger.LogWarning("ATR unavailable for symbol {SymbolId}, falling back to fixed % SL", symbolId);
        }

        return bot.DefaultStopLossPercent is decimal sl
            ? entryPrice * (1m - sl / 100m)
            : null;
    }

    private async Task<decimal?> ComputeAtrAsync(int symbolId, int period, CancellationToken cancellationToken)
    {
        var rows = await db.Candles
            .Where(c => c.SymbolId == symbolId)
            .OrderByDescending(c => c.OpenTime)
            .Take(period + 2)
            .Select(c => new { c.High, c.Low, c.Close, c.OpenTime })
            .ToListAsync(cancellationToken);
        if (rows.Count < period + 1) return null;
        rows.Reverse();
        var highs = rows.Select(r => r.High).ToList();
        var lows = rows.Select(r => r.Low).ToList();
        var closes = rows.Select(r => r.Close).ToList();
        return Indicators.Atr(highs, lows, closes, period);
    }

    private static (decimal? legacyTp, List<TpLevel> levels) BuildTpLevels(Bot? bot, decimal entryPrice, decimal originalQty)
    {
        if (bot is null) return (null, new List<TpLevel>());
        var templates = TpLevelsCodec.ParseTemplate(bot.TakeProfitLevelsJson);
        if (templates.Count > 0)
        {
            var levels = TpLevelsCodec.BuildFromTemplate(templates, entryPrice, originalQty);
            // legacyTp = first level's price for backwards compatibility with monitor's TakeProfitPrice
            var legacy = levels.Count > 0 ? levels[0].ClosePrice : (decimal?)null;
            return (legacy, levels);
        }
        var single = bot.DefaultTakeProfitPercent is decimal tp ? entryPrice * (1m + tp / 100m) : (decimal?)null;
        return (single, new List<TpLevel>());
    }
}
