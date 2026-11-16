namespace NexoCash.Domain.Entities;

public sealed class CloseDistribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CloseId { get; set; }
    public Guid AccountId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal Amount { get; set; }
    // Links the allocation to the movements that change balances.
    public Guid? OperationId { get; set; }
    public string? Concept { get; set; }
}
