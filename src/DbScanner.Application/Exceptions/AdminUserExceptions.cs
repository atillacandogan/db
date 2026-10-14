namespace DbScanner.Application.Exceptions;

public sealed class AdminUserOperationNotAllowedException(string message) : InvalidOperationException(message);

public sealed class IdentityUserNotFoundException(string userId)
    : InvalidOperationException("Identity user was not found.")
{
    public string UserId { get; } = userId;
}

public sealed class IdentityManagementUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class IdentityManagementConflictException(string message)
    : InvalidOperationException(message);

public sealed class IdentityEmailDeliveryException(string message)
    : Exception(message);
