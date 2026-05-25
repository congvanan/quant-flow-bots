using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Streaming;

namespace QuantFlowBots.Infrastructure.Notifications;

/// <summary>
/// Forwards critical bot events to each user's Telegram chat (if configured).
/// Hooks onto <see cref="InMemoryBotEventBus.OnEvent"/> so it does NOT compete
/// with the SignalR broadcaster on the primary channel.
/// </summary>
public sealed class TelegramNotifier(
    InMemoryBotEventBus botBus,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramNotifier> logger) : IHostedService
{
    private readonly ConcurrentDictionary<Guid, (UserSettings? settings, DateTimeOffset cachedAt)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> ForwardKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "risk", "auto_close",
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        botBus.OnEvent += HandleAsync;
        logger.LogInformation("TelegramNotifier subscribed to bot event bus.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        botBus.OnEvent -= HandleAsync;
        return Task.CompletedTask;
    }

    private async Task HandleAsync(BotEvent evt, CancellationToken cancellationToken)
    {
        if (!ForwardKinds.Contains(evt.Kind)) return;
        try
        {
            var userId = await ResolveUserIdAsync(evt.BotId, cancellationToken);
            if (userId == Guid.Empty) return;

            var settings = await GetSettingsAsync(userId, cancellationToken);
            if (settings is null || !settings.TelegramAlertsEnabled) return;
            if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId)) return;

            var text = $"🤖 *Quant Flow Bots*\n_{evt.Kind}_\nbot=`{evt.BotId}`\n{evt.Message}\nat {evt.At:HH:mm:ss}";
            using var client = httpClientFactory.CreateClient("telegram");
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = $"https://api.telegram.org/bot{settings.TelegramBotToken}/sendMessage";
            var payload = new
            {
                chat_id = settings.TelegramChatId,
                text,
                parse_mode = "Markdown",
                disable_web_page_preview = true,
            };
            using var resp = await client.PostAsJsonAsync(url, payload, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram returned {Status} for user {UserId}", resp.StatusCode, userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TelegramNotifier dispatch failed for bot {BotId}", evt.BotId);
        }
    }

    private async Task<Guid> ResolveUserIdAsync(Guid botId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        return await db.Bots.Where(b => b.Id == botId).Select(b => b.UserId).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<UserSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(userId, out var hit) && DateTimeOffset.UtcNow - hit.cachedAt < CacheTtl)
        {
            return hit.settings;
        }
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var s = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        _cache[userId] = (s, DateTimeOffset.UtcNow);
        return s;
    }
}
