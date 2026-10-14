namespace DbScanner.Domain.Entities;

/// <summary>Durable request snapshot, retained for safe replay after a worker failure.</summary>
public sealed class ScanOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }
    public required string Payload { get; set; }
    public bool DeadLetter { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? DispatchToken { get; set; }
    public DateTimeOffset? DispatchExpiresAt { get; set; }
    public int DeliveryAttempts { get; set; }
    public string? LastErrorCode { get; set; }
}
