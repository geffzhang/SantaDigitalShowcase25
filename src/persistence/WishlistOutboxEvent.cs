namespace Persistence;

public sealed class WishlistOutboxEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
