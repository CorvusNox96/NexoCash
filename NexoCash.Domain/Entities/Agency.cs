namespace NexoCash.Domain.Entities;

public sealed class Agency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? AdministrationNumber { get; set; }
    public byte[]? Photo { get; set; }
    // Suggested rate for new services; each service keeps its applied rate.
    public decimal? CurrentCommissionPercent { get; set; }
    public bool IsActive { get; set; } = true;
}
