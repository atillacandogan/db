namespace DbScanner.Domain.Enums;

public enum ProbeResultStatus
{
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    NotSupported = 4,
    InsufficientPrivileges = 5,
    ConnectionFailed = 6,
    AuthenticationFailed = 7
}
