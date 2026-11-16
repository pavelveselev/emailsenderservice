namespace EmailSenderService.Services;

/// <summary>
/// Вкладываемый файл. <paramref name="Content"/> принадлежит вызывающему: он читается при
/// измерении письма и ещё раз при отправке, поэтому должен оставаться открытым и поддерживать
/// перемотку до завершения <see cref="IEmailSender.SendAsync"/>, а затем вызывающий его освобождает.
/// </summary>
/// <param name="FileName">Уже очищено; используется как есть в MIME-параметрах имени.</param>
/// <param name="ContentType">Определяется этим сервисом, никогда не берётся у клиента.</param>
/// <param name="Content">Байты файла.</param>
public sealed record EmailAttachment(string FileName, string ContentType, Stream Content);
