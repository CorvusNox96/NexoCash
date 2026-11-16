namespace NexoCash.Domain.Entities;

public enum WorkdayStatus
{
    Open,
    Closed
}

public sealed class Workday
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public WorkdayStatus Status { get; set; } = WorkdayStatus.Open;
    public bool? WasWorked { get; set; }
    public string? NoWorkReason { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
