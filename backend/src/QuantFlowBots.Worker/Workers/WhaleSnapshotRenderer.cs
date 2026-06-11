using QuantFlowBots.Application.Exchanges;
using SkiaSharp;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Render PNG snapshot kiểu TradingView cho Whale alert:
///   - Panel TRÊN: candlestick (body Open-Close + wick High-Low). Xanh nếu Close≥Open, đỏ nếu ngược.
///   - Panel DƯỚI: volume bars cùng X-position với candle phía trên, baseline dashed = avg.
/// Chỉ DÙNG 2 MÀU (xanh / đỏ) — không gold border, không mũi tên, không tick O/C. Nến mới
/// nhất (= nến trigger) không được đánh dấu đặc biệt: nó luôn nằm ở rìa phải nên người xem
/// nhận diện bằng vị trí. Volume bar mới nhất có label số $ phía trên để verify giá trị.
/// </summary>
public static class WhaleSnapshotRenderer
{
    private const int Width = 620;
    private const int Height = 440;
    private const int PadX = 14;

    private const int HeaderH = 44;
    private const int PriceTop = 54;
    private const int PriceBottom = 270;
    private const int VolTop = 288;
    private const int VolBottom = 410;
    private const int FooterY = 428;

    private static readonly SKColor Bg = new(13, 17, 23);
    private static readonly SKColor UpColor = new(34, 163, 74);
    private static readonly SKColor DownColor = new(220, 56, 56);
    private static readonly SKColor TextDim = new(150, 158, 170);
    private static readonly SKColor TextBright = new(225, 230, 238);
    private static readonly SKColor Grid = new(40, 46, 56);
    private static readonly SKColor PanelDivider = new(28, 33, 42);

    public static byte[]? TryRender(
        string symbol, string interval, IReadOnlyList<CandleData> candles,
        decimal avgBaseline, decimal ratio, string direction, int maxBars = 30)
    {
        try
        {
            if (candles.Count < 2) return null;
            var shown = candles.Count > maxBars
                ? candles.Skip(candles.Count - maxBars).ToList()
                : candles.ToList();

            var maxPrice = shown.Max(c => c.High);
            var minPrice = shown.Min(c => c.Low);
            if (maxPrice <= minPrice) return null;
            var priceRange = maxPrice - minPrice;
            var pricePad = priceRange * 0.06m;
            maxPrice += pricePad; minPrice -= pricePad;
            priceRange = maxPrice - minPrice;

            var maxVol = shown.Max(c => c.QuoteVolume);
            if (maxVol <= 0m) return null;

            var lastIdx = shown.Count - 1;
            using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
            var c = surface.Canvas;
            c.Clear(Bg);

            using var fontMono = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default;
            using var fontSans = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;

            var isBuy = direction.Equals("buy", StringComparison.OrdinalIgnoreCase);
            var dirColor = isBuy ? UpColor : DownColor;

            // Header
            using (var dot = new SKPaint { Color = dirColor, IsAntialias = true })
                c.DrawCircle(PadX + 5, 18, 5, dot);
            using (var title = new SKPaint { Typeface = fontSans, TextSize = 15, Color = TextBright, IsAntialias = true, FakeBoldText = true })
                c.DrawText($"{symbol}", PadX + 16, 23, title);
            using (var meta = new SKPaint { Typeface = fontSans, TextSize = 12, Color = TextDim, IsAntialias = true })
                c.DrawText($"{interval} · whale {(isBuy ? "BUY" : "SELL")} · candles + volume", PadX + 16 + 90, 23, meta);
            using (var ratioP = new SKPaint { Typeface = fontSans, TextSize = 17, Color = dirColor, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Right })
                c.DrawText($"{ratio:0.0}× avg", Width - PadX, 24, ratioP);

            var plotW = Width - PadX * 2;
            var n = shown.Count;
            var slot = plotW / (float)n;
            var bodyW = Math.Max(3f, slot * 0.65f);

            // ===== Panel giá (candlestick) =====
            var priceH = PriceBottom - PriceTop;
            for (var k = 0; k <= 3; k++)
            {
                var y = PriceTop + priceH * k / 3f;
                using var gp = new SKPaint { Color = Grid, StrokeWidth = 1 };
                c.DrawLine(PadX, y, Width - PadX, y, gp);
                var labelPrice = maxPrice - priceRange * (decimal)k / 3m;
                using var lbl = new SKPaint { Typeface = fontMono, TextSize = 10, Color = TextDim, IsAntialias = true };
                c.DrawText(FmtPrice(labelPrice), PadX + 2, y - 3, lbl);
            }

            float PriceY(decimal p)
                => PriceTop + (float)((double)((maxPrice - p) / priceRange) * priceH);

            for (var i = 0; i < n; i++)
            {
                var cd = shown[i];
                var xCenter = PadX + i * slot + slot / 2f;
                var bodyX = xCenter - bodyW / 2f;
                // Quy tắc rõ ràng: xanh = close >= open, đỏ = close < open. Không có doji / gold / arrow.
                var bullish = cd.Close >= cd.Open;
                var col = bullish ? UpColor : DownColor;

                using (var wick = new SKPaint { Color = col, StrokeWidth = 1.2f, IsAntialias = true })
                    c.DrawLine(xCenter, PriceY(cd.High), xCenter, PriceY(cd.Low), wick);

                var yTop = PriceY(bullish ? cd.Close : cd.Open);
                var yBot = PriceY(bullish ? cd.Open : cd.Close);
                var bodyH = Math.Max(1.5f, yBot - yTop);
                using (var body = new SKPaint { Color = col, IsAntialias = true })
                    c.DrawRect(bodyX, yTop, bodyW, bodyH, body);
            }

            using (var divider = new SKPaint { Color = PanelDivider, StrokeWidth = 1 })
                c.DrawLine(PadX, (PriceBottom + VolTop) / 2f, Width - PadX, (PriceBottom + VolTop) / 2f, divider);
            using (var volLbl = new SKPaint { Typeface = fontMono, TextSize = 10, Color = TextDim, IsAntialias = true })
                c.DrawText("VOL", PadX + 2, VolTop + 10, volLbl);

            // ===== Panel volume =====
            var volH = VolBottom - VolTop;
            var baselineY = VolBottom - (float)((double)(avgBaseline / maxVol) * volH);

            for (var i = 0; i < n; i++)
            {
                var cd = shown[i];
                var xCenter = PadX + i * slot + slot / 2f;
                var bodyX = xCenter - bodyW / 2f;
                var h = (float)((double)(cd.QuoteVolume / maxVol) * volH);
                var y = VolBottom - h;
                var bullish = cd.Close >= cd.Open;
                var col = bullish ? UpColor : DownColor;
                using var p = new SKPaint { Color = col, IsAntialias = true };
                c.DrawRect(bodyX, y, bodyW, h, p);

                // Chỉ nến cuối (= nến trigger) hiển thị label $ phía trên — để verify giá trị
                // khớp với "Order Size" trong text. Không dùng màu vàng / border đặc biệt.
                if (i == lastIdx)
                {
                    using var sv = new SKPaint { Typeface = fontMono, TextSize = 11, Color = TextBright, IsAntialias = true, TextAlign = SKTextAlign.Center, FakeBoldText = true };
                    c.DrawText(FmtUsd(cd.QuoteVolume), xCenter, y - 5, sv);
                }
            }

            using (var dash = new SKPaint { Color = TextDim, IsStroke = true, StrokeWidth = 1.2f, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0) })
                c.DrawLine(PadX, baselineY, Width - PadX, baselineY, dash);
            using (var lbl = new SKPaint { Typeface = fontMono, TextSize = 10, Color = TextDim, IsAntialias = true })
                c.DrawText($"avg {FmtUsd(avgBaseline)}", PadX + 30, baselineY - 3, lbl);

            using (var axis = new SKPaint { Color = Grid, StrokeWidth = 1 })
                c.DrawLine(PadX, VolBottom, Width - PadX, VolBottom, axis);

            using (var foot = new SKPaint { Typeface = fontSans, TextSize = 11, Color = TextDim, IsAntialias = true })
                c.DrawText($"last {n} × {interval} · same-direction baseline ({(isBuy ? "buy" : "sell")} candles only)", PadX, FooterY, foot);

            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 95);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string FmtUsd(decimal n)
    {
        if (n >= 1_000_000_000m) return $"${n / 1_000_000_000m:0.00}B";
        if (n >= 1_000_000m) return $"${n / 1_000_000m:0.00}M";
        if (n >= 1_000m) return $"${n / 1_000m:0.0}K";
        return $"${n:0}";
    }

    private static string FmtPrice(decimal p)
    {
        if (p >= 1000) return p.ToString("N2");
        if (p >= 1) return p.ToString("F4");
        return p.ToString("G6");
    }
}
