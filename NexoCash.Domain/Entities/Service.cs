namespace NexoCash.Domain.Entities;

public enum ServiceType
{
    Agency = 0,
    StreetPickup = 2,
    OneOffCustomer = 3
}

public enum ServiceStatus
{
    Open = 0,
    Finished = 2
}

public enum PaymentMethod
{
    Cash,
    Transfer
}

public sealed class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkdayId { get; set; }
    public ServiceType Type { get; set; }
    public ServiceStatus Status { get; set; } = ServiceStatus.Open;
    public PaymentMethod PaymentMethod { get; set; }
    public Guid? AgencyId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal? ExpectedPrice { get; set; }
    public decimal? CollectedAmount { get; set; }
    // Snapshot of this service commission; later agency rate changes do not affect it.
    public decimal? AppliedCommissionPercent { get; set; }
    public decimal? CommissionAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Description { get; set; }
}
