namespace QuantFlowBots.Domain.Entities;

public sealed class Exchange
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RestBaseUrl { get; set; } = string.Empty;
    public string WebSocketBaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<Symbol> Symbols { get; set; } = new List<Symbol>();
}
