using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Security;

namespace QuantFlowBots.Infrastructure.Trading.LiveTrading;

/// <summary>
/// Live executor for Binance Spot Testnet.
///   ENTRY (Buy): spot OrderValidator (uses symbols.StepSize/MinQty/MinNotional) → MARKET BUY →
///                Position row (Mode=Live, ExchangePositionRef=symbol) → register with PositionMonitor
///                so SL/TP/trailing run via TradingDispatcher just like paper.
///   EXIT (Sell): cancel any open orders on the symbol → MARKET SELL the position qty →
///                update Position row.
/// NOTE: SL/TP are NOT placed server-side on spot in this phase. PositionMonitor inside the
/// same API/Worker process protects them. If the process dies, position is unprotected
/// (testnet only — acceptable). Real-money spot should add OCO in a follow-up.
/// </summary>
public sealed class LiveSpotExecutor(
    QuantFlowBotsDbContext db,
    IApiKeyEncryption encryption,
    BinanceSpotSignedClient spot,
    PositionMonitor monitor,
    IBotEventBus botBus,
    ILogger<LiveSpotExecutor> logger)
{
    public async Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest req, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.Include(b => b.Symbol).FirstOrDefaultAsync(b => b.Id == req.BotId, cancellationToken);
        if (bot is null) return Reject(req, "bot_not_found");
        if (bot.Symbol is null) return Reject(req, "symbol_missing");

        // Multi-account: lệnh con mang req.ApiKeyId; đóng lệnh dùng account của vị thế. null = legacy.
        var keyId = req.ApiKeyId ?? bot.ApiKeyId;
        if (req.Side == OrderSide.Sell)
        {
            var posKey = await db.Positions
                .Where(p => p.BotId == bot.Id && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open
                    && p.Mode == TradingMode.Live && (req.PositionId == null || p.Id == req.PositionId))
                .Select(p => p.ApiKeyId).FirstOrDefaultAsync(cancellationToken);
            if (posKey is not null) keyId = posKey;
        }
        if (keyId is null) return Reject(req, "no_api_key_linked");

        var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key?.Exchange is null) return Reject(req, "api_key_or_exchange_missing");

        var cred = new SpotCredential(
            encryption.Decrypt(key.EncryptedKey),
            encryption.Decrypt(key.EncryptedSecret),
            key.Exchange.RestBaseUrl);

        return req.Side == OrderSide.Buy
            ? await OpenAsync(req, bot, cred, keyId.Value, cancellationToken)
            : await CloseAsync(req, bot, cred, cancellationToken);
    }

    private async Task<PaperOrderResult> OpenAsync(PaperOrderRequest req, Bot bot, SpotCredential cred, Guid keyId, CancellationToken cancellationToken)
    {
        var symbol = bot.Symbol!;
        var validation = OrderValidator.Validate(symbol, req.Quantity, req.Price);
        if (!validation.Approved) return Reject(req, $"spot_filter:{validation.Reason}");

        var clientOrderId = $"qfb_s_{Guid.NewGuid():N}"[..32];
        var orderRow = new Order
        {
            BotId = req.BotId,
            BotRunId = req.BotRunId,
            ApiKeyId = keyId,
            SymbolId = req.SymbolId,
            Mode = TradingMode.Live,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Status = OrderStatus.New,
            Price = validation.AdjustedPrice,
            Quantity = validation.AdjustedQuantity,
            ClientOrderId = clientOrderId,
        };
        db.Orders.Add(orderRow);
        await db.SaveChangesAsync(cancellationToken);

        SpotOrderResult entry;
        try
        {
            entry = await spot.PlaceMarketOrderAsync(cred, symbol.Code, OrderSide.Buy, validation.AdjustedQuantity, clientOrderId, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistFailureAsync(orderRow, ex.Message, cancellationToken);
        }

        orderRow.Status = entry.Status;
        orderRow.ExchangeOrderId = entry.ExchangeOrderId;
        orderRow.AveragePrice = entry.AveragePrice > 0 ? entry.AveragePrice : validation.AdjustedPrice;
        orderRow.FilledQuantity = entry.FilledQuantity > 0 ? entry.FilledQuantity : validation.AdjustedQuantity;
        orderRow.Commission = entry.Commission;
        orderRow.RejectReason = entry.RejectReason;
        if (entry.Status == OrderStatus.Filled) orderRow.FilledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (entry.Status != OrderStatus.Filled && entry.Status != OrderStatus.PartiallyFilled)
            return new PaperOrderResult(orderRow.Id, entry.Status, 0m, 0m, 0m, entry.RejectReason);

        var entryPrice = orderRow.AveragePrice;
        decimal? slPrice = bot.DefaultStopLossPercent.HasValue
            ? entryPrice * (1m - bot.DefaultStopLossPercent.Value / 100m) : null;
        decimal? tpPrice = bot.DefaultTakeProfitPercent.HasValue
            ? entryPrice * (1m + bot.DefaultTakeProfitPercent.Value / 100m) : null;

        var pos = new Position
        {
            BotId = bot.Id,
            BotRunId = req.BotRunId,
            ApiKeyId = keyId,
            SymbolId = req.SymbolId,
            Mode = TradingMode.Live,
            Side = PositionSide.Long,
            Status = PositionStatus.Open,
            Quantity = orderRow.FilledQuantity,
            OriginalQuantity = orderRow.FilledQuantity,
            EntryPrice = entryPrice,
            StopLossPrice = slPrice,
            TakeProfitPrice = tpPrice,
            TrailingStopPercent = bot.DefaultTrailingStopPercent,
            ExchangePositionRef = symbol.Code,
        };
        db.Positions.Add(pos);
        await db.SaveChangesAsync(cancellationToken);

        // Hand off to PositionMonitor (in-process SL/TP/trailing). Monitor will call
        // TradingDispatcher → us again with Side=Sell when SL/TP triggers.
        await monitor.AddAsync(new MonitoredPosition(
            pos.Id, bot.Id, pos.SymbolId, symbol.Code, PositionSide.Long,
            pos.Quantity, pos.OriginalQuantity, entryPrice,
            pos.StopLossPrice, pos.TakeProfitPrice, pos.TrailingStopPercent, entryPrice));

        await botBus.PublishAsync(new BotEvent(bot.Id, "order",
            $"LIVE SPOT entry filled qty={orderRow.FilledQuantity:F6} @ {entryPrice:F4} (sl={slPrice:F4} tp={tpPrice:F4} — in-process)",
            DateTimeOffset.UtcNow), cancellationToken);
        logger.LogInformation("Spot entry filled bot={BotId} {Symbol} qty={Qty} @ {Px}", bot.Id, symbol.Code, orderRow.FilledQuantity, entryPrice);
        return new PaperOrderResult(orderRow.Id, OrderStatus.Filled, orderRow.FilledQuantity, entryPrice, 0m, null);
    }

    private async Task<PaperOrderResult> CloseAsync(PaperOrderRequest req, Bot bot, SpotCredential cred, CancellationToken cancellationToken)
    {
        var symbol = bot.Symbol!.Code;
        try { await spot.CancelOpenOrdersAsync(cred, symbol, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "CancelOpenOrders failed bot={BotId}", bot.Id); }

        var clientOrderId = $"qfb_sx_{Guid.NewGuid():N}"[..32];
        var orderRow = new Order
        {
            BotId = req.BotId,
            BotRunId = req.BotRunId,
            ApiKeyId = req.ApiKeyId,
            SymbolId = req.SymbolId,
            Mode = TradingMode.Live,
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Status = OrderStatus.New,
            Price = req.Price,
            Quantity = req.Quantity,
            ClientOrderId = clientOrderId,
        };
        db.Orders.Add(orderRow);
        await db.SaveChangesAsync(cancellationToken);

        SpotOrderResult exit;
        try
        {
            exit = await spot.PlaceMarketOrderAsync(cred, symbol, OrderSide.Sell, req.Quantity, clientOrderId, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistFailureAsync(orderRow, ex.Message, cancellationToken);
        }

        orderRow.Status = exit.Status;
        orderRow.ExchangeOrderId = exit.ExchangeOrderId;
        orderRow.AveragePrice = exit.AveragePrice > 0 ? exit.AveragePrice : req.Price;
        orderRow.FilledQuantity = exit.FilledQuantity > 0 ? exit.FilledQuantity : req.Quantity;
        orderRow.Commission = exit.Commission;
        if (exit.Status == OrderStatus.Filled) orderRow.FilledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var pos = await db.Positions
            .Where(p => p.BotId == bot.Id && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open && p.Mode == TradingMode.Live
                && (req.PositionId == null || p.Id == req.PositionId))
            .FirstOrDefaultAsync(cancellationToken);
        decimal pnl = 0m;
        if (pos is not null)
        {
            pos.Status = PositionStatus.Closed;
            pos.ExitPrice = orderRow.AveragePrice;
            pos.ClosedAt = DateTimeOffset.UtcNow;
            pos.CloseReason = req.Reason;
            pnl = (orderRow.AveragePrice - pos.EntryPrice) * pos.Quantity;
            pos.RealizedPnl = pnl;
            await db.SaveChangesAsync(cancellationToken);
            await monitor.RemoveAsync(pos.Id);
        }

        await botBus.PublishAsync(new BotEvent(bot.Id, "auto_close",
            $"LIVE SPOT exit {req.Reason} qty={orderRow.FilledQuantity:F6} @ {orderRow.AveragePrice:F4} pnl={pnl:F2}",
            DateTimeOffset.UtcNow), cancellationToken);
        return new PaperOrderResult(orderRow.Id, exit.Status, orderRow.FilledQuantity, orderRow.AveragePrice, pnl, null);
    }

    private async Task<PaperOrderResult> PersistFailureAsync(Order orderRow, string error, CancellationToken cancellationToken)
    {
        orderRow.Status = OrderStatus.Rejected;
        orderRow.RejectReason = error.Length > 512 ? error[..512] : error;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogError("Live spot order failed bot={BotId} {Reason}", orderRow.BotId, orderRow.RejectReason);
        return new PaperOrderResult(orderRow.Id, OrderStatus.Rejected, 0m, 0m, 0m, orderRow.RejectReason);
    }

    private PaperOrderResult Reject(PaperOrderRequest req, string reason)
    {
        logger.LogWarning("LiveSpotExecutor rejected bot={BotId}: {Reason}", req.BotId, reason);
        return new PaperOrderResult(Guid.Empty, OrderStatus.Rejected, 0m, 0m, 0m, reason);
    }
}
