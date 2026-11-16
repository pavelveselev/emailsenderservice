namespace EmailSenderService.Models;

/// <param name="MessageId">Сгенерированный для письма заголовок Message-Id; по нему можно сопоставить с логами релея.</param>
/// <param name="RecipientCount">Всего адресов в конверте (To + Cc + Bcc).</param>
/// <param name="SentAtUtc">Момент, когда релей принял письмо.</param>
public sealed record SendEmailResponse(string MessageId, int RecipientCount, DateTimeOffset SentAtUtc);
