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

    public override string Kind => KindCode;
    public override int WarmupBars => Math.Max(_maPeriod + 3, _vwapAnchor switch
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

        // Không vào lệnh chồng nếu đang có position mở (counter-trend dễ thua khi pyramid).
        if (context.OpenPositionQuantity is decimal q && q != 0m) return null;

        var quality = new Dictionary<string, object?>
        {
            ["ma"] = Math.Round(ma.Value, 6),
            ["vwap"] = Math.Round(vwap.Value, 6),
            ["priceMaDistPct"] = Math.Round(priceMaDistPct, 4),
            ["maVwapDistPct"] = Math.Round(maVwapDistPct, 4),
            ["maPeriod"] = _maPeriod,
            ["vwapAnchor"] = _vwapAnchor,
        };

        // Stretched UP — overheated → counter-trend SELL.
        if (priceMaDistPct >= _priceMaDistancePct && maVwapDistPct >= _maVwapDistancePct
            && (_direction is "sell" or "both"))
        {
            return new StrategyDecision(SignalType.Entry, OrderSide.Sell, currClose, 1m,
                $"stretched↑ price {priceMaDistPct:F2}% above MA{_maPeriod}, MA {maVwapDistPct:F2}% above VWAP",
                quality);
        }

        // Stretched DOWN — oversold → counter-trend BUY.
        if (priceMaDistPct <= -_priceMaDistancePct && maVwapDistPct <= -_maVwapDistancePct
            && (_direction is "buy" or "both"))
        {
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, currClose, 1m,
                $"stretched↓ price {priceMaDistPct:F2}% below MA{_maPeriod}, MA {maVwapDistPct:F2}% below VWAP",
                quality);
        }

        return null;
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
