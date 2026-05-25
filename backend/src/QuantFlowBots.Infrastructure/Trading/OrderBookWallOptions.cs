namespace QuantFlowBots.Infrastructure.Trading;

/// <summary>
/// Tuning for the order-book wall scanner. Defaults are sane for spot USDT pairs
/// on a $50k account watching majors. Override in appsettings under "OrderBookWalls".
/// </summary>
public sealed class OrderBookWallOptions
{
    /// <summary>How often the worker re-scans depth (seconds).</summary>
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>Top-N symbols by 24h quote volume that the scanner watches each tick.</summary>
    public int MaxSymbols { get; set; } = 50;

    /// <summary>Depth `limit` parameter passed to /api/v3/depth (5..5000).</summary>
    public int DepthLimit { get; set; } = 100;

    /// <summary>Default value of the user-facing filter (FE/API default if caller passes none).</summary>
    public decimal MinNotionalUsdt { get; set; } = 200_000_000m;

    /// <summary>Hard floor the worker actually caches — anything below this is dropped at scan time.
    /// Keep low so user can drag the FE slider down to small walls without restarting the worker.
    /// $10K is low enough to see retail-scale walls on small caps, while still filtering noise.</summary>
    public decimal DetectionFloorUsdt { get; set; } = 10_000m;

    /// <summary>Skip walls farther than this % from mid (they rarely matter for short-term price).</summary>
    public decimal MaxDistanceFromMidPercent { get; set; } = 2.0m;

    /// <summary>Quote asset whitelist; empty = all.</summary>
    public string[] QuoteAssets { get; set; } = ["USDT"];

    /// <summary>Base assets to skip when scanning USDT pairs; these are stable/pegged assets, not altcoin markets.
    /// Kept as belt-and-suspenders alongside the dynamic price-band filter below.</summary>
    public string[] ExcludedBaseAssets { get; set; } =
    [
        "USDT", "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "USDD", "DAI", "SUSD",
        "USD1", "RLUSD", "PYUSD", "GUSD", "LUSD", "PAX", "UST", "USTC",
        "EUR", "EURI", "AEUR", "EURT", "EURC", "TRY", "BRL", "GBP"
    ];

    /// <summary>Dynamic stable-pair filter: skip any symbol whose mid price sits within
    /// ±this% of $1.00. Catches future stablecoins (RLUSD, UUSDT, ...) without code change.
    /// Set to 0 to disable.</summary>
    public decimal StablePairPriceBandPercent { get; set; } = 3.0m;
}
