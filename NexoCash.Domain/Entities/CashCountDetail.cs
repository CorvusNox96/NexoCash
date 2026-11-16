namespace NexoCash.Domain.Entities;

public sealed class CashCountDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CashCountId { get; set; }
    public Guid DenominationId { get; set; }
    public int Quantity { get; set; }
}
