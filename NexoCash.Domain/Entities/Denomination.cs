namespace NexoCash.Domain.Entities;

public sealed class Denomination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CurrencyCode { get; set; }
    public decimal Value { get; set; }
    public bool IsActive { get; set; } = true;
}
