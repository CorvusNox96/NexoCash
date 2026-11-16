namespace NexoCash.Domain.Entities;

public enum CashCountType
{
    TotalCash,
    DailyMovement,
    Reconciliation
}

public sealed class CashCount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CountedAt { get; set; } = DateTimeOffset.UtcNow;
    public CashCountType Type { get; set; }
    public Guid? WorkdayId { get; set; }
    public Guid? CloseId { get; set; }
    public string? Observaciones { get; set; }
}
