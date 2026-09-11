namespace EmailSenderService.Services;

/// <summary>Raised when the upstream relay rejected or could not be reached; surfaced as HTTP 502.</summary>
public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
