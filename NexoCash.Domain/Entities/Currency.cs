namespace NexoCash.Domain.Entities;

public sealed class Currency
{
    // ISO code, such as CUP or USD.
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsPrimaryCurrency { get; set; }
}
