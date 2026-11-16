namespace NexoCash.Domain.Entities;

public enum DistributionRuleType
{
    Percentage,
    FixedAmount
}

public sealed class DistributionRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid DestinationAccountId { get; set; }
    public DistributionRuleType Type { get; set; }
    public int Priority { get; set; }
    public decimal Value { get; set; }
    // For fixed rules, such as vehicle rent.
    public string? CurrencyCode { get; set; }
    public bool AppliesOnSundays { get; set; } = true;
    public bool AppliesOnNonWorkingDays { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
