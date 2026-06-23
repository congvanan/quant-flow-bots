namespace QuantFlowBots.Domain.Entities;

/// <summary>
/// Một account Binance tham gia vào 1 bot (multi-account fan-out). Mỗi BotAccount như một
/// "mini-bot": vốn riêng (BaseEquityUsdt), trọng số riêng (Weight) và kill-switch riêng —
/// chốt model "Independent sizing + per-account risk" 2026-06-22.
///
/// Sizing per account khi bot vào lệnh:
///   accountQty = baseQty × (account.BaseEquityUsdt / bot.BaseEquityUsdt) × account.Weight
/// → Weight bằng nhau = chia đều theo vốn; Weight lệch = dial up/down từng account.
///
/// Bot KHÔNG có dòng BotAccount nào → chạy single-account legacy qua Bot.ApiKeyId (không đổi).
/// Khi có >=1 dòng active → dispatcher fan-out, mỗi account đặt 1 lệnh con tagged ApiKeyId.
/// </summary>
public sealed class BotAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BotId { get; set; }
    public Guid ApiKeyId { get; set; }
    public string Label { get; set; } = string.Empty;

    // Trọng số nhân lên size sau khi đã quy theo vốn. Default 1 = đúng tỷ lệ vốn.
    public decimal Weight { get; set; } = 1m;
    // Vốn độc lập của account này cho bot. Là cơ sở để size lệnh + tính daily-loss riêng.
    public decimal BaseEquityUsdt { get; set; } = 1000m;
    public bool IsActive { get; set; } = true;

    // Per-account kill-switch (risk scope = riêng từng account). Khi tripped → bỏ qua account
    // này trong fan-out, các account khác vẫn vào lệnh. Reset thủ công qua API.
    public DateTimeOffset? KillSwitchTrippedAt { get; set; }
    public string? KillSwitchReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Bot? Bot { get; set; }
    public ApiKey? ApiKey { get; set; }
}
