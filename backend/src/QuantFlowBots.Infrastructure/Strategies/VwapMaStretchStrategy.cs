using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Strategies;

/// <summary>
/// VWAP-MA STRETCH (mean-reversion): entry khi GIÁ và MA cùng nới rộng so với VWAP — tức
/// "cảm xúc" (MA) đã đi xa "lý trí" (VWAP), và giá thậm chí xa MA → khả năng đảo chiều cao.
///
/// Khác với <see cref="VwapEmotionCrossStrategy"/> (cross-based, vào lệnh theo chiều breakout
/// khi giá vừa cắt MA): chiến lược này là COUNTER-TREND, vào lệnh ngược chiều stretch:
///   - Stretched UP (overheated): Price ≥ MA × (1 + X%) AND MA ≥ VWAP × (1 + Y%) → SELL
///   - Stretched DOWN (oversold): Price ≤ MA × (1 - X%) AND MA ≤ VWAP × (1 - Y%) → BUY
///
/// **Lưu ý "giá hiện tại chưa kết thúc nến"**: framework hiện fire OnCandle khi nến đóng,
/// nên <c>currClose</c> = close nến vừa đóng (proxy gần nhất với "giá hiện tại"). Để bám sát
/// tick hơn, config bot interval=1m. Trade-off: 1m noise cao hơn → bật filter context (vd
/// volume, sentiment) bù lại.
/// </summary>
public sealed class VwapMaStretchStrategy : StrategyBase
{
    public const string KindCode = "vwap_ma_stretch";

    private int _maPeriod = 20;
    private string _vwapAnchor = "daily";
    private decimal _priceMaDistancePct = 5m;
    private decimal _maVwapDistancePct = 5m;
    private string _direction = "both";
    // Exit 2 tầng theo trạng thái lệnh (spec user 2026-06-06):
    // - TP: giá hồi về MA (mean reversion hoàn thành) → chốt.
    // - Break-even (OPTIONAL — breakEvenEnabled): lệnh đã từng dương >= breakEvenArmPct →
    //   SL dời về entry, giá quay về entry → thoát hòa vốn. Tắt thì maxLossPct áp dụng
    //   xuyên suốt kể cả khi lệnh từng có lãi.
    // - Lệnh lỗ (hoặc BE tắt) → SL cứng theo maxLossPct từ entry.
    private decimal _maxLossPct = 3m;
    private bool _breakEvenEnabled = true;
    private decimal _breakEvenArmPct = 0.5m;
    // Bollinger Band filter (spec user 2026-06-06): entry BUY cần giá nằm trong vùng
    // bbDistancePct% quanh (hoặc dưới) band LOWER — xác nhận oversold theo volatility thực.
    // Short (futures): mirror với band UPPER. BB chuẩn TradingView: SMA(close, period) ± mult×σ
    // (population stddev).
    private bool _bbEnabled = true;
    private int _bbPeriod = 20;
    private decimal _bbStdDev = 2m;
    private decimal _bbDistancePct = 1m;
    // State per-position: lệnh hiện tại đã từng đạt lãi >= arm threshold chưa. Strategy instance
    // là per-bot (live) / per-run (backtest), xử lý nến tuần tự → instance field an toàn.
    private bool _breakEvenArmed;

    public override string Kind => KindCode;
    public override int WarmupBars => Math.Max(
        Math.Max(_maPeriod, _bbEnabled ? _bbPeriod : 0) + 3,
        _vwapAnchor switch
        {
            "weekly" => 168,
            "monthly" => 360,
            _ => 24,
        });

    public override void Configure(IReadOnlyDictionary<string, object?> p)
    {
        _maPeriod = Math.Clamp(Int(p, "maPeriod", 20), 5, 200);
        _vwapAnchor = (Str(p, "vwapAnchor", "daily") ?? "daily").ToLowerInvariant();
        if (_vwapAnchor is not ("daily" or "weekly" or "monthly")) _vwapAnchor = "daily";
        // Clamp ngưỡng vào dải hợp lý — 0.5% quá nhỏ sẽ nổ signal liên tục; 25% quá lớn không
        // bao giờ trigger trong điều kiện thường.
        _priceMaDistancePct = Math.Clamp(Dec(p, "priceMaDistancePct", 5m), 0.5m, 25m);
        _maVwapDistancePct = Math.Clamp(Dec(p, "maVwapDistancePct", 5m), 0.5m, 25m);
        _direction = (Str(p, "direction", "both") ?? "both").ToLowerInvariant();
        if (_direction is not ("buy" or "sell" or "both")) _direction = "both";
        _maxLossPct = Math.Clamp(Dec(p, "maxLossPct", 3m), 0.2m, 50m);
        _breakEvenEnabled = Bool(p, "breakEvenEnabled", true);
        _breakEvenArmPct = Math.Clamp(Dec(p, "breakEvenArmPct", 0.5m), 0.05m, 10m);
        _bbEnabled = Bool(p, "bbEnabled", true);
        _bbPeriod = Math.Clamp(Int(p, "bbPeriod", 20), 5, 200);
        _bbStdDev = Math.Clamp(Dec(p, "bbStdDev", 2m), 0.5m, 5m);
        _bbDistancePct = Math.Clamp(Dec(p, "bbDistancePct", 1m), 0.05m, 20m);
    }

    public override StrategyDecision? OnCandle(CandleData candle, IStrategyContext context)
    {
        var history = context.History;
        if (history.Count < WarmupBars) return null;

        var n = history.Count;
        var currClose = candle.Close;

        var ma = SmaClose(history, n - 1, _maPeriod);
        if (ma is null || ma.Value <= 0m) return null;

        var vwap = AnchoredVwap(history, n - 1, _vwapAnchor);
        if (vwap is null || vwap.Value <= 0m) return null;

        // Khoảng cách ký (signed): dương = ABOVE, âm = BELOW. Tỷ lệ %.
        var priceMaDistPct = (currClose - ma.Value) / ma.Value * 100m;
        var maVwapDistPct = (ma.Value - vwap.Value) / vwap.Value * 100m;

        // Đang giữ position (long — backtest engine + spot bot chỉ hỗ trợ long):
        // quản lý EXIT thay vì return null vô điều kiện. Trước đây return null mãi →
        // position ôm đến cuối data, backtest chỉ ra đúng 1 lệnh Sell(eod).
        if (context.OpenPositionQuantity is decimal q && q > 0m)
        {
            var entry = context.OpenPositionEntryPrice ?? 0m;
            if (entry <= 0m) return null; // không có entry price → không quản lý exit được

            // ARM break-even (chỉ khi breakEvenEnabled): nến này (High intra-candle) đã từng
            // lãi >= ngưỡng arm → SL dời về entry (hòa vốn), không còn dùng maxLossPct nữa.
            // Tắt BE → _breakEvenArmed mãi false → maxLossPct áp dụng xuyên suốt.
            var peakProfitPct = (candle.High - entry) / entry * 100m;
            if (_breakEvenEnabled && peakProfitPct >= _breakEvenArmPct) _breakEvenArmed = true;

            // Stops check TRƯỚC TP — stop order khớp intra-candle (Low chạm) trước khi
            // nến đóng, còn TP của ta là close-based (chờ nến đóng xác nhận chạm MA).
            if (_breakEvenArmed && candle.Low <= entry)
            {
                // BREAK-EVEN: lệnh từng dương nhưng giá quay về entry → thoát hòa vốn.
                // Exit price = entry (mô phỏng stop order đặt tại entry, BacktestRunner
                // dùng decision.Price làm giá khớp).
                _breakEvenArmed = false;
                return new StrategyDecision(SignalType.Exit, OrderSide.Sell, entry, 1m,
                    $"break-even stop: từng lãi {peakProfitPct:F2}% nhưng giá quay về entry {entry}", null);
            }
            if (!_breakEvenArmed)
            {
                // MAX-LOSS: lệnh lỗ ngay từ đầu (chưa từng dương) → SL cứng theo config.
                var slPrice = entry * (1m - _maxLossPct / 100m);
                if (candle.Low <= slPrice)
                {
                    return new StrategyDecision(SignalType.Exit, OrderSide.Sell, slPrice, 1m,
                        $"max-loss stop: giá chạm −{_maxLossPct:F2}% từ entry {entry}", null);
                }
            }

            // TAKE-PROFIT: giá đã hồi về MA (hoặc vượt) → mean reversion hoàn thành.
            if (priceMaDistPct >= 0m)
            {
                _breakEvenArmed = false;
                return new StrategyDecision(SignalType.Exit, OrderSide.Sell, currClose, 1m,
                    $"mean-reverted: price chạm MA{_maPeriod} (dist {priceMaDistPct:F2}%)", null);
            }
            return null; // giữ lệnh, chờ revert
        }
        // SHORT position (futures mode — positionQty âm): exit logic mirror của long.
        // Short lãi khi giá GIẢM: TP khi giá hồi xuống chạm MA từ trên, BE arm khi Low từng
        // xuống dưới entry × (1 − armPct), max-loss khi High vượt entry × (1 + maxLossPct).
        if (context.OpenPositionQuantity is decimal qs && qs < 0m)
        {
            var entry = context.OpenPositionEntryPrice ?? 0m;
            if (entry <= 0m) return null;

            var peakProfitPct = (entry - candle.Low) / entry * 100m;
            if (_breakEvenEnabled && peakProfitPct >= _breakEvenArmPct) _breakEvenArmed = true;

            if (_breakEvenArmed && candle.High >= entry)
            {
                _breakEvenArmed = false;
                return new StrategyDecision(SignalType.Exit, OrderSide.Buy, entry, 1m,
                    $"break-even stop (short): từng lãi {peakProfitPct:F2}% nhưng giá quay về entry {entry}", null);
            }
            if (!_breakEvenArmed)
            {
                var slPrice = entry * (1m + _maxLossPct / 100m);
                if (candle.High >= slPrice)
                {
                    return new StrategyDecision(SignalType.Exit, OrderSide.Buy, slPrice, 1m,
                        $"max-loss stop (short): giá chạm +{_maxLossPct:F2}% từ entry {entry}", null);
                }
            }
            // TP: giá hồi xuống chạm MA (mean reversion từ phía trên hoàn thành).
            if (priceMaDistPct <= 0m)
            {
                _breakEvenArmed = false;
                return new StrategyDecision(SignalType.Exit, OrderSide.Buy, currClose, 1m,
                    $"mean-reverted (short): price chạm MA{_maPeriod} (dist {priceMaDistPct:F2}%)", null);
            }
            return null;
        }

        // Không có position → reset arm state cho lệnh kế tiếp.
        _breakEvenArmed = false;

        // Bollinger Band filter: BUY cần giá nằm trong vùng bbDistancePct% quanh/dưới band LOWER
        // (xác nhận oversold theo volatility); SHORT mirror với band UPPER.
        var nearBbLower = true;
        var nearBbUpper = true;
        decimal? bbLower = null, bbUpper = null;
        if (_bbEnabled)
        {
            var bb = Bollinger(history, n - 1, _bbPeriod, _bbStdDev);
            if (bb is null) return null; // chưa đủ data cho BB
            (bbUpper, bbLower) = (bb.Value.Upper, bb.Value.Lower);
            nearBbLower = currClose <= bbLower.Value * (1m + _bbDistancePct / 100m);
            nearBbUpper = currClose >= bbUpper.Value * (1m - _bbDistancePct / 100m);
        }

        var quality = new Dictionary<string, object?>
        {
            ["ma"] = Math.Round(ma.Value, 6),
            ["vwap"] = Math.Round(vwap.Value, 6),
            ["priceMaDistPct"] = Math.Round(priceMaDistPct, 4),
            ["maVwapDistPct"] = Math.Round(maVwapDistPct, 4),
            ["maPeriod"] = _maPeriod,
            ["vwapAnchor"] = _vwapAnchor,
            ["bbLower"] = bbLower is null ? null : Math.Round(bbLower.Value, 6),
            ["bbUpper"] = bbUpper is null ? null : Math.Round(bbUpper.Value, 6),
        };

        // Stretched UP + giá sát band UPPER — overheated → counter-trend SELL/SHORT (futures).
        if (priceMaDistPct >= _priceMaDistancePct && maVwapDistPct >= _maVwapDistancePct
            && nearBbUpper
            && (_direction is "sell" or "both"))
        {
            return new StrategyDecision(SignalType.Entry, OrderSide.Sell, currClose, 1m,
                $"stretched↑ price {priceMaDistPct:F2}% above MA{_maPeriod}, MA {maVwapDistPct:F2}% above VWAP" +
                (_bbEnabled ? $", sát BB upper {bbUpper:F4}" : ""),
                quality);
        }

        // Stretched DOWN + giá sát band LOWER — oversold → counter-trend BUY (spot + futures).
        if (priceMaDistPct <= -_priceMaDistancePct && maVwapDistPct <= -_maVwapDistancePct
            && nearBbLower
            && (_direction is "buy" or "both"))
        {
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, currClose, 1m,
                $"stretched↓ price {priceMaDistPct:F2}% below MA{_maPeriod}, MA {maVwapDistPct:F2}% below VWAP" +
                (_bbEnabled ? $", sát BB lower {bbLower:F4}" : ""),
                quality);
        }

        return null;
    }

    /// <summary>BB chuẩn TradingView: SMA(close, period) ± mult × population-σ.</summary>
    private static (decimal Upper, decimal Lower)? Bollinger(
        IReadOnlyList<CandleData> history, int endIndex, int period, decimal mult)
    {
        if (endIndex - period + 1 < 0) return null;
        decimal sum = 0m;
        for (var i = endIndex - period + 1; i <= endIndex; i++) sum += history[i].Close;
        var mean = sum / period;
        decimal varSum = 0m;
        for (var i = endIndex - period + 1; i <= endIndex; i++)
        {
            var d = history[i].Close - mean;
            varSum += d * d;
        }
        var std = (decimal)Math.Sqrt((double)(varSum / period));
        return (mean + mult * std, mean - mult * std);
    }

    private static decimal? SmaClose(IReadOnlyList<CandleData> history, int endIndex, int period)
    {
        if (endIndex - period + 1 < 0) return null;
        decimal sum = 0m;
        for (var i = endIndex - period + 1; i <= endIndex; i++) sum += history[i].Close;
        return sum / period;
    }

    private static decimal? AnchoredVwap(IReadOnlyList<CandleData> history, int endIndex, string anchor)
    {
        var endOpen = history[endIndex].OpenTime;
        var anchorStart = anchor switch
        {
            "weekly" => endOpen.AddDays(-(int)endOpen.DayOfWeek).Date,
            "monthly" => new DateTime(endOpen.Year, endOpen.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => endOpen.Date,
        };
        decimal pv = 0m, v = 0m;
        for (var i = endIndex; i >= 0; i--)
        {
            if (history[i].OpenTime < anchorStart) break;
            var typical = (history[i].High + history[i].Low + history[i].Close) / 3m;
            pv += typical * history[i].Volume;
            v += history[i].Volume;
        }
        return v <= 0m ? null : pv / v;
    }
}
