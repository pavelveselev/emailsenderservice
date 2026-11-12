using EmailSenderService.Models;

namespace EmailSenderService.Services;

public interface IEmailSender
{
    /// <param name="request">Recipients, subject and body.</param>
    /// <param name="attachments">Files to attach, or null for a plain message. The caller owns their streams.</param>
    /// <param name="cancellationToken">Aborts building or sending.</param>
    /// <exception cref="MessageTooLargeException">The built message exceeds the relay's size limit; nothing was sent.</exception>
    /// <exception cref="EmailDeliveryException">The relay rejected the message or was unreachable.</exception>
    Task<SendEmailResponse> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken);
}
