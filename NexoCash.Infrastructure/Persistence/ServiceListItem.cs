using System.Globalization;

namespace NexoCash.Infrastructure.Persistence;

public sealed record ServiceListItem(
    DateOnly Date,
    string Status,
    string Currency,
    decimal? ExpectedPrice,
    decimal? CollectedAmount,
    decimal? CommissionAmount,
    string? Description)
{
    public string DateText => Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
}
