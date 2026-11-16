namespace NexoCash.Domain.Entities;

public enum AgencyFundMovementType
{
    TopUp,
    ServiceFee,
    Adjustment
}

public sealed class AgencyFundMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgencyId { get; set; }
    public Guid? ServiceId { get; set; }
    // For a top-up, links the outgoing movement from an owned account.
    public Guid? AccountMovementId { get; set; }
    public required string CurrencyCode { get; set; }
    // Positive increases the fund; negative reduces it.
    public decimal Amount { get; set; }
    public AgencyFundMovementType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Description { get; set; }
}
