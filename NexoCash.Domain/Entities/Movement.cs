namespace NexoCash.Domain.Entities;

public enum MovementType
{
    Income,
    Expense,
    Transfer,
    Exchange,
    Adjustment
}

public sealed class Movement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public required string CurrencyCode { get; set; }
    // Positive adds money to the account; negative removes it.
    public decimal Amount { get; set; }
    public MovementType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? WorkdayId { get; set; }
    public Guid? ServiceId { get; set; }
    // Groups both sides of a transfer or currency exchange.
    public Guid? OperationId { get; set; }
    public string? Description { get; set; }
}
