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
/// Live executor against Binance Futures testnet — full lifecycle:
///   ENTRY (Buy): set leverage → validate qty against futures filters → MARKET buy →
///                place server-side STOP_MARKET + TAKE_PROFIT_MARKET reduce-only →
///                persist Position with exchange order refs.
///   EXIT (Sell): cancel any open conditional orders for the symbol → MARKET sell →
///                update Position row.
/// Caller has already passed ILiveTradingGate (api key + withdraw guard).
/// </summary>
public sealed class LiveFuturesExecutor(
    QuantFlowBotsDbContext db,
    IApiKeyEncryption encryption,
    BinanceFuturesRestClient futures,
    FuturesSymbolFiltersCache filters,
    IBotEventBus botBus,
    ILogger<LiveFuturesExecutor> logger)
{
    public async Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest req, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.Include(b => b.Symbol).FirstOrDefaultAsync(b => b.Id == req.BotId, cancellationToken);
        if (bot is null) return Reject(req, "bot_not_found");
        if (bot.Symbol is null) return Reject(req, "symbol_missing");

        // Multi-account: lệnh con mang req.ApiKeyId. Đóng lệnh thì account = chính vị thế đang đóng
        // (mới chính xác). null → single-account legacy = bot.ApiKeyId.
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

        var key = await db.ApiKeys.Include(k => k.Exchange)
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key?.Exchange is null) return Reject(req, "api_key_or_exchange_missing");

        var cred = new FuturesCredential(
            encryption.Decrypt(key.EncryptedKey),
            encryption.Decrypt(key.EncryptedSecret),
            key.Exchange.RestBaseUrl);

        return req.Side == OrderSide.Buy
            ? await OpenAsync(req, bot, cred, keyId.Value, cancellationToken)
            : await CloseAsync(req, bot, cred, cancellationToken);
    }

    private async Task<PaperOrderResult> OpenAsync(
        PaperOrderRequest req, Bot bot, FuturesCredential cred, Guid keyId, CancellationToken cancellationToken)
    {
        var symbol = bot.Symbol!.Code;
        var validation = await filters.ValidateAsync(cred.BaseUrl!, symbol, req.Quantity, req.Price, cancellationToken);
        if (!validation.Ok) return Reject(req, $"futures_filter:{validation.Reason}");

        // Set leverage (idempotent on exchange side; cheap)
        if (bot.Leverage > 0)
        {
            try { await futures.SetLeverageAsync(cred, symbol, bot.Leverage, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "SetLeverage failed (continuing): bot={BotId}", bot.Id); }
        }

        var clientOrderId = $"qfb_{Guid.NewGuid():N}"[..32];
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

        FuturesOrderResult entry;
        try
        {
            entry = await futures.PlaceMarketOrderAsync(
                cred, symbol, OrderSide.Buy, validation.AdjustedQuantity, clientOrderId, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistFailureAsync(orderRow, ex.Message, cancellationToken);
        }

        orderRow.Status = entry.Status;
        orderRow.ExchangeOrderId = entry.ExchangeOrderId;
        orderRow.AveragePrice = entry.AveragePrice > 0 ? entry.AveragePrice : validation.AdjustedPrice;
        orderRow.FilledQuantity = entry.FilledQuantity > 0 ? entry.FilledQuantity : validation.AdjustedQuantity;
        orderRow.RejectReason = entry.RejectReason;
        if (entry.Status == OrderStatus.Filled) orderRow.FilledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (entry.Status != OrderStatus.Filled && entry.Status != OrderStatus.PartiallyFilled)
            return new PaperOrderResult(orderRow.Id, entry.Status, 0m, 0m, 0m, entry.RejectReason);

        // Server-side SL/TP (reduce-only) so position is protected even if our process dies.
        var entryPrice = orderRow.AveragePrice;
        decimal? slPrice = bot.DefaultStopLossPercent.HasValue
            ? entryPrice * (1m - bot.DefaultStopLossPercent.Value / 100m) : null;
        decimal? tpPrice = bot.DefaultTakeProfitPercent.HasValue
            ? entryPrice * (1m + bot.DefaultTakeProfitPercent.Value / 100m) : null;

        string? slId = null, tpId = null;
        if (slPrice.HasValue)
        {
            var slCoid = $"qfb_sl_{Guid.NewGuid():N}"[..32];
            var slResult = await futures.PlaceConditionalAsync(cred, symbol, OrderSide.Sell, orderRow.FilledQuantity,
                slPrice.Value, slCoid, isTakeProfit: false, cancellationToken);
            if (slResult.Status == OrderStatus.Rejected)
                logger.LogWarning("SL placement rejected bot={BotId}: {Reason}", bot.Id, slResult.RejectReason);
            else
                slId = slResult.ExchangeOrderId;
        }
        if (tpPrice.HasValue)
        {
            var tpCoid = $"qfb_tp_{Guid.NewGuid():N}"[..32];
            var tpResult = await futures.PlaceConditionalAsync(cred, symbol, OrderSide.Sell, orderRow.FilledQuantity,
                tpPrice.Value, tpCoid, isTakeProfit: true, cancellationToken);
            if (tpResult.Status == OrderStatus.Rejected)
                logger.LogWarning("TP placement rejected bot={BotId}: {Reason}", bot.Id, tpResult.RejectReason);
            else
                tpId = tpResult.ExchangeOrderId;
        }

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
            ExchangePositionRef = symbol,
            ExchangeStopOrderId = slId,
            ExchangeTpOrderId = tpId,
        };
        db.Positions.Add(pos);
        await db.SaveChangesAsync(cancellationToken);

        await botBus.PublishAsync(new BotEvent(bot.Id, "order",
            $"LIVE entry filled qty={orderRow.FilledQuantity:F6} @ {entryPrice:F4} (sl={slPrice:F4} tp={tpPrice:F4})",
            DateTimeOffset.UtcNow), cancellationToken);
        logger.LogInformation("Live entry filled bot={BotId} {Symbol} qty={Qty} @ {Px}", bot.Id, symbol, orderRow.FilledQuantity, entryPrice);
        return new PaperOrderResult(orderRow.Id, OrderStatus.Filled, orderRow.FilledQuantity, entryPrice, 0m, null);
    }

    private async Task<PaperOrderResult> CloseAsync(
        PaperOrderRequest req, Bot bot, FuturesCredential cred, CancellationToken cancellationToken)
    {
        var symbol = bot.Symbol!.Code;
        // Cancel any leftover conditional orders first (avoid double-fill race).
        try { await futures.CancelAllOpenOrdersAsync(cred, symbol, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "CancelAllOpenOrders failed bot={BotId}", bot.Id); }

        var clientOrderId = $"qfb_x_{Guid.NewGuid():N}"[..32];
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

        FuturesOrderResult exit;
        try
        {
            exit = await futures.PlaceMarketOrderAsync(cred, symbol, OrderSide.Sell, req.Quantity, clientOrderId, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistFailureAsync(orderRow, ex.Message, cancellationToken);
        }

        orderRow.Status = exit.Status;
        orderRow.ExchangeOrderId = exit.ExchangeOrderId;
        orderRow.AveragePrice = exit.AveragePrice > 0 ? exit.AveragePrice : req.Price;
        orderRow.FilledQuantity = exit.FilledQuantity > 0 ? exit.FilledQuantity : req.Quantity;
        if (exit.Status == OrderStatus.Filled) orderRow.FilledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var pos = await db.Positions
            .Where(p => p.BotId == bot.Id && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open && p.Mode == TradingMode.Live
                && (req.PositionId == null || p.Id == req.PositionId))
            .FirstOrDefaultAsync(cancellationToken);
        if (pos is not null)
        {
            pos.Status = PositionStatus.Closed;
            pos.ExitPrice = orderRow.AveragePrice;
            pos.ClosedAt = DateTimeOffset.UtcNow;
            pos.CloseReason = req.Reason;
            pos.RealizedPnl = (orderRow.AveragePrice - pos.EntryPrice) * pos.Quantity;
            await db.SaveChangesAsync(cancellationToken);
        }

        await botBus.PublishAsync(new BotEvent(bot.Id, "auto_close",
            $"LIVE exit {req.Reason} qty={orderRow.FilledQuantity:F6} @ {orderRow.AveragePrice:F4}",
            DateTimeOffset.UtcNow), cancellationToken);
        return new PaperOrderResult(orderRow.Id, exit.Status, orderRow.FilledQuantity, orderRow.AveragePrice, pos?.RealizedPnl ?? 0m, null);
    }

    private async Task<PaperOrderResult> PersistFailureAsync(Order orderRow, string error, CancellationToken cancellationToken)
    {
        orderRow.Status = OrderStatus.Rejected;
        orderRow.RejectReason = error.Length > 512 ? error[..512] : error;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogError("Live futures order failed bot={BotId} {Reason}", orderRow.BotId, orderRow.RejectReason);
        return new PaperOrderResult(orderRow.Id, OrderStatus.Rejected, 0m, 0m, 0m, orderRow.RejectReason);
    }

    private PaperOrderResult Reject(PaperOrderRequest req, string reason)
    {
        logger.LogWarning("LiveFuturesExecutor rejected bot={BotId}: {Reason}", req.BotId, reason);
        return new PaperOrderResult(Guid.Empty, OrderStatus.Rejected, 0m, 0m, 0m, reason);
    }
}
