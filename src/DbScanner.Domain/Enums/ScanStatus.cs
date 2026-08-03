namespace DbScanner.Domain.Enums;

public enum ScanStatus
{
    Pending = 1,
    Scanning = 2,
    Scanned = 3,
    Failed = 4,
    Timeout = 5,
    Cancelled = 6
}
