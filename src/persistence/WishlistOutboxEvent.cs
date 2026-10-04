namespace Persistence;

public sealed class WishlistOutboxEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Category { get; set; }
    public double? BudgetEstimate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? StatusChange { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
