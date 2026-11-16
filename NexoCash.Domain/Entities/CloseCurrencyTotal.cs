namespace NexoCash.Domain.Entities;

public sealed class CloseCurrencyTotal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CloseId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetResult { get; set; }
}
