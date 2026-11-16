namespace NexoCash.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Address { get; set; }
    public byte[]? Photo { get; set; }
    public bool IsActive { get; set; } = true;
}
