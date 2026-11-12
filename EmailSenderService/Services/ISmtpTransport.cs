using EmailSenderService.Options;
using MimeKit;

namespace EmailSenderService.Services;

/// <summary>
/// The wire step only: hand a finished message to the relay. Kept apart from message building
/// so tests can capture the exact <see cref="MimeMessage"/> without an SMTP server.
/// </summary>
public interface ISmtpTransport
{
    /// <returns>The relay's final response line, for the log.</returns>
    /// <exception cref="EmailDeliveryException">The relay rejected the message or was unreachable.</exception>
    Task<string> SendAsync(MimeMessage message, SmtpOptions options, CancellationToken cancellationToken);
}
