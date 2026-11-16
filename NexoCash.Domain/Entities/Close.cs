namespace NexoCash.Domain.Entities;

public sealed class Close
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkdayId { get; set; }
    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Observaciones { get; set; }
}
