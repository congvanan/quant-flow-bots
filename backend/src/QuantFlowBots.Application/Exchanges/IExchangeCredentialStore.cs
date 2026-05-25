using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Exchanges;

public sealed record ExchangeCredential(
    Guid ApiKeyId,
    int ExchangeId,
    string ExchangeCode,
    string Label,
    string ApiKey,
    string ApiSecret,
    TradingMode Mode);

public interface IExchangeCredentialStore
{
    Task<ExchangeCredential?> GetActiveAsync(
        Guid userId,
        string exchangeCode,
        TradingMode mode,
        CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid apiKeyId, CancellationToken cancellationToken);
    Task MarkErrorAsync(Guid apiKeyId, string error, CancellationToken cancellationToken);
}
