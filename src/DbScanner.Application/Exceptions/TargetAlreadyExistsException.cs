namespace DbScanner.Application.Exceptions;

public sealed class TargetAlreadyExistsException : InvalidOperationException
{
    public const string ErrorCode = "TARGET_ALREADY_EXISTS";
    public const string UserMessage = "Bu servis zaten target listesinde bulunuyor.";

    public TargetAlreadyExistsException(Guid? existingTargetId, Exception? innerException = null)
        : base(UserMessage, innerException)
    {
        ExistingTargetId = existingTargetId;
    }

    public Guid? ExistingTargetId { get; }
}
