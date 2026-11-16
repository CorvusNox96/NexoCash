namespace NexoCash.Domain.Entities;

public sealed class BanknoteMovementDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovimientoId { get; set; }
    public Guid DenominationId { get; set; }
    // Always positive; the movement sign indicates cash in or out.
    public int Quantity { get; set; }
}
