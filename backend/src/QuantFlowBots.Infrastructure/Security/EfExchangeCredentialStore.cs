using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Security;

public sealed class EfExchangeCredentialStore(
    QuantFlowBotsDbContext db,
    IApiKeyEncryption encryption) : IExchangeCredentialStore
{
    public async Task<ExchangeCredential?> GetActiveAsync(
        Guid userId,
        string exchangeCode,
        TradingMode mode,
        CancellationToken cancellationToken)
    {
        var normalizedExchange = exchangeCode.Trim().ToLowerInvariant();
        var key = await db.ApiKeys
            .Include(k => k.Exchange)
            .Where(k =>
                k.UserId == userId &&
                k.IsActive &&
                k.Mode == mode &&
                k.Exchange != null &&
                k.Exchange.Code == normalizedExchange)
            .OrderByDescending(k => k.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (key?.Exchange is null) return null;

        return new ExchangeCredential(
            key.Id,
            key.ExchangeId,
            key.Exchange.Code,
            key.Label,
            encryption.Decrypt(key.EncryptedKey),
            encryption.Decrypt(key.EncryptedSecret),
            key.Mode);
    }

    public async Task MarkUsedAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
        if (key is null) return;
        key.LastUsedAt = DateTimeOffset.UtcNow;
        key.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkErrorAsync(Guid apiKeyId, string error, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
        if (key is null) return;
        key.LastError = error.Length > 512 ? error[..512] : error;
        key.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
