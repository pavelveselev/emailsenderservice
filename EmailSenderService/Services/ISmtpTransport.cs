using EmailSenderService.Options;
using MimeKit;

namespace EmailSenderService.Services;

/// <summary>
/// Только сетевой шаг: передать готовое письмо релею. Отделён от сборки письма, чтобы тесты
/// могли перехватить точный <see cref="MimeMessage"/> без SMTP-сервера.
/// </summary>
public interface ISmtpTransport
{
    /// <returns>Итоговая строка ответа релея, для лога.</returns>
    /// <exception cref="EmailDeliveryException">Релей отклонил письмо или был недоступен.</exception>
    Task<string> SendAsync(MimeMessage message, SmtpOptions options, CancellationToken cancellationToken);
}
