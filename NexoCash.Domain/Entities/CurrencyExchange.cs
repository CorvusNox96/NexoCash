namespace NexoCash.Domain.Entities;

public sealed class CurrencyExchange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutgoingMovementId { get; set; }
    public Guid IncomingMovementId { get; set; }
    // Actual rate obtained, independent of the reference rate.
    public decimal AppliedRate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Description { get; set; }
}
