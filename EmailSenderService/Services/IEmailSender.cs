using EmailSenderService.Models;

namespace EmailSenderService.Services;

public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">The relay rejected the message or was unreachable.</exception>
    Task<SendEmailResponse> SendAsync(SendEmailRequest request, CancellationToken cancellationToken);
}
