namespace DbScanner.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string RequestedQueue { get; set; } = "dbscanner.scan.requested";
    public string RetryQueue { get; set; } = "dbscanner.scan.retry";
    public string DeadLetterQueue { get; set; } = "dbscanner.scan.deadletter";
    public string NseScriptSyncQueue { get; set; } = "dbscanner.nse-script.sync";
    public string DiscoveryRequestedQueue { get; set; } = "dbscanner.discovery.requested";
    public string DiscoveryDeadLetterQueue { get; set; } = "dbscanner.discovery.deadletter";
    public int NseScriptSyncTimeoutSeconds { get; set; } = 60;
    public ushort PrefetchCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 5000;
    public int MaxRetryCount { get; set; } = 3;
}
