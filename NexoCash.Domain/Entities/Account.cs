namespace NexoCash.Domain.Entities;

public enum AccountType
{
    Cash,
    Virtual
}

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public bool IsActive { get; set; } = true;
}
