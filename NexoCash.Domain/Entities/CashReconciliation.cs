namespace NexoCash.Domain.Entities;

public sealed class CashReconciliation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CashCountId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal CountedAmount { get; set; }
    // Movement in the special account for unresolved cash differences.
    // Null when the total amount matches and only denominations change.
    public Guid? AdjustmentMovementId { get; set; }
    public DateTimeOffset ReconciledAt { get; set; } = DateTimeOffset.UtcNow;
}
