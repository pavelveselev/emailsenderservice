using EmailSenderService.Models;

namespace EmailSenderService.Services;

public interface IEmailSender
{
    /// <param name="request">Получатели, тема и тело.</param>
    /// <param name="attachments">Вкладываемые файлы или null для письма без вложений. Их потоками владеет вызывающий.</param>
    /// <param name="cancellationToken">Прерывает сборку или отправку.</param>
    /// <exception cref="MessageTooLargeException">Собранное письмо превышает лимит размера релея; ничего не отправлено.</exception>
    /// <exception cref="EmailDeliveryException">Релей отклонил письмо или был недоступен.</exception>
    Task<SendEmailResponse> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken);
}
