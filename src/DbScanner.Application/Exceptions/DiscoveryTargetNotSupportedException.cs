namespace DbScanner.Application.Exceptions;

public sealed class DiscoveryTargetNotSupportedException : InvalidOperationException
{
    public const string ErrorCode = "TARGET_NOT_SUPPORTED";
    public const string UserMessage = "Desteklenmeyen servis target olarak eklenemez.";

    public DiscoveryTargetNotSupportedException() : base(UserMessage)
    {
    }
}
