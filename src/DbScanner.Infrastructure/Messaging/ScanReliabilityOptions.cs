namespace DbScanner.Infrastructure.Messaging;

public sealed class ScanReliabilityOptions
{
    public const string SectionName = "ScanReliability";
    public int LeaseSeconds { get; set; } = 60;
    public int HeartbeatSeconds { get; set; } = 10;
    public int RecoveryIntervalSeconds { get; set; } = 15;
    public int MaxExecutionAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int OutboxPollSeconds { get; set; } = 1;
    public int DispatchLeaseSeconds { get; set; } = 30;
    public int PublishTimeoutSeconds { get; set; } = 10;
}
