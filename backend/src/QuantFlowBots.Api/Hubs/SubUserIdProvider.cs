using Microsoft.AspNetCore.SignalR;

namespace QuantFlowBots.Api.Hubs;

/// SignalR UserIdentifier = JWT 'sub'. Cần custom vì Program.cs đặt MapInboundClaims=false nên
/// 'sub' KHÔNG tự map sang ClaimTypes.NameIdentifier (mặc định của DefaultUserIdProvider) →
/// Context.UserIdentifier sẽ null. Nhờ provider này, Clients.User(userId) và group "user:{sub}"
/// (join trong MarketHub.OnConnectedAsync) hoạt động đúng.
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value;
}
