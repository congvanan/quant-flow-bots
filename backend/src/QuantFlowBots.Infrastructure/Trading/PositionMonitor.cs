using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed class PositionMonitor(
    IServiceScopeFactory scopeFactory,
    IBotEventBus botBus,
    ILogger<PositionMonitor> logger)
{
    private readonly ConcurrentDictionary<Guid, MonitoredPosition> _byId = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, MonitoredPosition>> _bySymbol = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> WatchedSymbols => _bySymbol.Keys.ToList();
    public event Func<Task>? SubscriptionChanged;

    public async Task LoadOpenPositionsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var open = await db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .Include(p => p.Symbol)
            .Include(p => p.Bot)
            .ToListAsync(cancellationToken);

        foreach (var p in open)
        {
            if (p.Symbol is null) continue;
            var levels = TpLevelsCodec.Parse(p.TakeProfitLevelsJson);
            var mp = new MonitoredPosition(
                p.Id, p.BotId, p.SymbolId, p.Symbol.Code, p.Side,
                p.Quantity, p.OriginalQuantity > 0 ? p.OriginalQuantity : p.Quantity,
                p.EntryPrice, p.StopLossPrice, p.TakeProfitPrice,
                p.TrailingStopPercent, p.HighestPriceSinceEntry ?? p.EntryPrice)
            {
                TpLevels = levels,
                BreakEvenEnabled = p.Bot?.BreakEvenEnabled ?? false,
                BreakEvenTriggerPercent = p.Bot?.BreakEvenTriggerPercent,
                BreakEvenOffsetPercent = p.Bot?.BreakEvenOffsetPercent ?? 0.1m,
                BreakEvenTriggered = p.BreakEvenTriggered,
            };
            Add(mp);
        }
        logger.LogInformation("PositionMonitor loaded {Count} open positions across {Symbols} symbols.", _byId.Count, _bySymbol.Count);
        await NotifySubscriptionAsync();
    }

    public Task AddAsync(MonitoredPosition position)
    {
        Add(position);
        return NotifySubscriptionAsync();
    }

    private void Add(MonitoredPosition position)
    {
        _byId[position.PositionId] = position;
        _bySymbol.GetOrAdd(position.SymbolCode, _ => new ConcurrentDictionary<Guid, MonitoredPosition>())[position.PositionId] = position;
    }

    public Task RemoveAsync(Guid positionId)
    {
        if (_byId.TryRemove(positionId, out var p))
        {
            if (_bySymbol.TryGetValue(p.SymbolCode, out var inner))
            {
                inner.TryRemove(positionId, out _);
                if (inner.IsEmpty) _bySymbol.TryRemove(p.SymbolCode, out _);
            }
        }
        return NotifySubscriptionAsync();
    }

    private Task NotifySubscriptionAsync() => SubscriptionChanged?.Invoke() ?? Task.CompletedTask;

    public async Task OnBookTickerAsync(BookTickerEvent evt, CancellationToken cancellationToken)
    {
        if (!_bySymbol.TryGetValue(evt.Symbol, out var positions)) return;
        var midPrice = (evt.BestBid + evt.BestAsk) / 2m;
        foreach (var p in positions.Values)
        {
            await EvaluateAsync(p, midPrice, evt.BestBid, evt.BestAsk, cancellationToken);
        }
    }

    private async Task EvaluateAsync(MonitoredPosition p, decimal mid, decimal bid, decimal ask, CancellationToken cancellationToken)
    {
        if (p.Side != PositionSide.Long) return;

        // 1. Trailing stop: lift SL when price hits new high
        if (p.TrailingStopPercent.HasValue && mid > p.HighestPrice)
        {
            p.HighestPrice = mid;
            var newSl = mid * (1m - p.TrailingStopPercent.Value / 100m);
            if (!p.StopLossPrice.HasValue || newSl > p.StopLossPrice.Value)
            {
                p.StopLossPrice = newSl;
                _ = PersistTrailingAsync(p.PositionId, newSl, mid, cancellationToken);
            }
        }

        // 2. Break-even shift
        if (p.BreakEvenEnabled && !p.BreakEvenTriggered && p.BreakEvenTriggerPercent is decimal trigger)
        {
            var profitPct = (mid - p.EntryPrice) / p.EntryPrice * 100m;
            if (profitPct >= trigger)
            {
                var newSl = p.EntryPrice * (1m + p.BreakEvenOffsetPercent / 100m);
                if (!p.StopLossPrice.HasValue || newSl > p.StopLossPrice.Value)
                {
                    p.StopLossPrice = newSl;
                }
                p.BreakEvenTriggered = true;
                logger.LogInformation("PositionMonitor break-even triggered {Pos} @ {Mid:F4}, SL -> {Sl:F4}", p.PositionId, mid, newSl);
                _ = PersistBreakEvenAsync(p.PositionId, p.StopLossPrice, cancellationToken);
                _ = botBus.PublishAsync(new BotEvent(p.BotId, "auto_close",
                    $"break-even @ {mid:F4}, SL -> {p.StopLossPrice:F4}", DateTimeOffset.UtcNow), cancellationToken);
            }
        }

        // 3. Stop loss check (highest priority among exits)
        if (p.StopLossPrice.HasValue && bid <= p.StopLossPrice.Value)
        {
            await CloseAsync(p, bid, "stop_loss", cancellationToken);
            return;
        }

        // 4. Multi-level TP — partial close on each unhit level
        if (p.TpLevels.Count > 0)
        {
            await ProcessTpLevelsAsync(p, bid, cancellationToken);
            return;
        }

        // 5. Legacy single TP fallback
        if (p.TakeProfitPrice.HasValue && bid >= p.TakeProfitPrice.Value)
        {
            await CloseAsync(p, bid, "take_profit", cancellationToken);
        }
    }

    private async Task ProcessTpLevelsAsync(MonitoredPosition p, decimal bid, CancellationToken cancellationToken)
    {
        // Find the next unhit level that triggers at this price
        TpLevel? hit = null;
        var idx = -1;
        for (var i = 0; i < p.TpLevels.Count; i++)
        {
            var lv = p.TpLevels[i];
            if (lv.HitAt is null && bid >= lv.ClosePrice)
            {
                hit = lv;
                idx = i;
                break;
            }
        }
        if (hit is null) return;

        if (!p.TryClose()) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();
            var qty = Math.Min(hit.CloseQty, p.Quantity);
            var allRemaining = qty >= p.Quantity;
            var reason = allRemaining ? $"take_profit_final" : $"take_profit_lvl_{idx + 1}";
            var result = await executor.ExecuteAsync(new PaperOrderRequest(
                p.BotId, null, p.SymbolId, OrderSide.Sell, qty, bid, $"auto:{reason}",
                PositionId: p.PositionId), cancellationToken);

            // Mark level hit + persist
            var updated = new TpLevel(hit.ProfitPercent, hit.ClosePercent, hit.ClosePrice, hit.CloseQty, DateTimeOffset.UtcNow);
            p.TpLevels[idx] = updated;
            p.Quantity -= qty;
            await PersistTpLevelsAsync(p.PositionId, p.TpLevels, cancellationToken);

            await botBus.PublishAsync(new BotEvent(p.BotId, "auto_close",
                $"TP{idx + 1} @ {bid:F4} qty={qty:F6} pnl={result.RealizedPnl:F2}", DateTimeOffset.UtcNow), cancellationToken);

            if (allRemaining || p.Quantity <= 0)
            {
                await RemoveAsync(p.PositionId);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PositionMonitor TP level partial-close failed {Pos}", p.PositionId);
        }
        finally
        {
            p.AllowReclose();
        }
    }

    private async Task CloseAsync(MonitoredPosition p, decimal bid, string reason, CancellationToken cancellationToken)
    {
        if (!p.TryClose()) return;
        logger.LogInformation("PositionMonitor closing {Pos} ({Symbol}) on {Reason} at {Bid:F4}", p.PositionId, p.SymbolCode, reason, bid);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();
            var result = await executor.ExecuteAsync(new PaperOrderRequest(
                p.BotId, null, p.SymbolId, OrderSide.Sell, p.Quantity, bid, $"auto:{reason}",
                PositionId: p.PositionId), cancellationToken);

            using var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
            var posRow = await db.Positions.FirstOrDefaultAsync(x => x.Id == p.PositionId, cancellationToken);
            if (posRow is not null && posRow.Status != PositionStatus.Open)
            {
                posRow.CloseReason = reason;
                await db.SaveChangesAsync(cancellationToken);
            }

            await botBus.PublishAsync(new BotEvent(p.BotId, "auto_close",
                $"{reason} @ {bid:F4} qty={p.Quantity} pnl={result.RealizedPnl:F2}", DateTimeOffset.UtcNow), cancellationToken);
            await RemoveAsync(p.PositionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PositionMonitor failed closing {Pos}", p.PositionId);
            p.AllowReclose();
        }
    }

    private async Task PersistTrailingAsync(Guid positionId, decimal newSl, decimal newHigh, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
            var p = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId, cancellationToken);
            if (p is null) return;
            p.StopLossPrice = newSl;
            p.HighestPriceSinceEntry = newHigh;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch { /* best-effort */ }
    }

    private async Task PersistBreakEvenAsync(Guid positionId, decimal? newSl, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
            var p = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId, cancellationToken);
            if (p is null) return;
            p.BreakEvenTriggered = true;
            if (newSl.HasValue) p.StopLossPrice = newSl.Value;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch { /* best-effort */ }
    }

    private async Task PersistTpLevelsAsync(Guid positionId, List<TpLevel> levels, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
            var p = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId, cancellationToken);
            if (p is null) return;
            p.TakeProfitLevelsJson = TpLevelsCodec.Serialize(levels);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch { /* best-effort */ }
    }
}

public sealed class MonitoredPosition(
    Guid positionId,
    Guid botId,
    int symbolId,
    string symbolCode,
    PositionSide side,
    decimal quantity,
    decimal originalQuantity,
    decimal entryPrice,
    decimal? stopLossPrice,
    decimal? takeProfitPrice,
    decimal? trailingStopPercent,
    decimal highestPrice)
{
    private int _closing;

    public Guid PositionId { get; } = positionId;
    public Guid BotId { get; } = botId;
    public int SymbolId { get; } = symbolId;
    public string SymbolCode { get; } = symbolCode;
    public PositionSide Side { get; } = side;
    public decimal Quantity { get; set; } = quantity;
    public decimal OriginalQuantity { get; } = originalQuantity;
    public decimal EntryPrice { get; } = entryPrice;
    public decimal? StopLossPrice { get; set; } = stopLossPrice;
    public decimal? TakeProfitPrice { get; set; } = takeProfitPrice;
    public decimal? TrailingStopPercent { get; } = trailingStopPercent;
    public decimal HighestPrice { get; set; } = highestPrice;

    public List<TpLevel> TpLevels { get; set; } = new();
    public bool BreakEvenEnabled { get; set; }
    public decimal? BreakEvenTriggerPercent { get; set; }
    public decimal BreakEvenOffsetPercent { get; set; } = 0.1m;
    public bool BreakEvenTriggered { get; set; }

    public bool TryClose() => Interlocked.CompareExchange(ref _closing, 1, 0) == 0;
    public void AllowReclose() => Interlocked.Exchange(ref _closing, 0);
}
