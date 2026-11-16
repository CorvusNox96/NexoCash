namespace NexoCash.Domain.Entities;

public sealed class ExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FromCurrencyCode { get; set; }
    public required string ToCurrencyCode { get; set; }
    // One unit of the source currency equals this amount of the target currency.
    public decimal Value { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}
