using EmailSenderService.Models;
using EmailSenderService.Options;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.IO;
using MimeKit.Utils;

namespace EmailSenderService.Services;

/// <summary>
/// Собирает <see cref="MimeMessage"/>, проверяет его на лимит размера релея и передаёт в
/// <see cref="ISmtpTransport"/>. Этот API - единственное место до самого релея, где этот лимит
/// соблюдается, поэтому проверяется полностью закодированное письмо, а не оценка.
/// </summary>
public sealed class SmtpEmailSender(
    IOptionsMonitor<SmtpOptions> optionsMonitor,
    ISmtpTransport transport,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    // SMTP передаёт CRLF; FormatOptions.Default использует перевод строки платформы, из-за чего
    // на Linux каждая строка base64 недосчитывалась бы на один байт.
    private static readonly FormatOptions WireFormat = CreateWireFormat();

    public async Task<SendEmailResponse> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        // Читаем через монитор при каждой отправке, чтобы сменённые учётные данные начинали
        // действовать сразу после перезагрузки источника конфигурации.
        var options = optionsMonitor.CurrentValue;

        // Не освобождаем: Dispose письма закрыл бы потоки вложений, а они принадлежат вызывающему
        // коду и освобождаются им после возврата из этого метода.
        var message = BuildMessage(request, attachments, options);

        var messageBytes = await MeasureAsync(message, cancellationToken);
        if (messageBytes > options.MaxMessageBytes)
        {
            logger.LogWarning("Refused {MessageId}: {MessageBytes} bytes encoded, limit {LimitBytes}",
                message.MessageId, messageBytes, options.MaxMessageBytes);
            throw new MessageTooLargeException(messageBytes, options.MaxMessageBytes);
        }

        var response = await transport.SendAsync(message, options, cancellationToken);

        var recipientCount = message.To.Count + message.Cc.Count + message.Bcc.Count;
        logger.LogInformation(
            "Sent {MessageId} ({MessageBytes} bytes) to {RecipientCount} recipient(s) via {Host}:{Port}. Relay response: {Response}",
            message.MessageId, messageBytes, recipientCount, options.Host, options.Port, response);

        return new SendEmailResponse(message.MessageId, recipientCount, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Сначала фиксирует кодировку передачи каждой части под самый строгий (7-битный) транспорт,
    /// чтобы измеренный здесь размер совпадал с тем, что уйдёт в сеть, - собственный Prepare в
    /// MailKit не трогает части с явно заданной кодировкой. Bcc здесь ещё записывается, поэтому
    /// результат слегка завышен.
    /// </summary>
    private static async Task<long> MeasureAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        message.Prepare(EncodingConstraint.SevenBit);

        await using var measuring = new MeasuringStream();
        await message.WriteToAsync(WireFormat, measuring, cancellationToken);
        return measuring.Length;
    }

    private static MimeMessage BuildMessage(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment>? attachments,
        SmtpOptions options)
    {
        var message = new MimeMessage { MessageId = MimeUtils.GenerateMessageId() };
        message.From.Add(new MailboxAddress(options.FromDisplayName ?? string.Empty, options.FromAddress));
        message.To.AddRange(request.To.Select(MailboxAddress.Parse));

        if (request.Cc is { Count: > 0 })
        {
            message.Cc.AddRange(request.Cc.Select(MailboxAddress.Parse));
        }

        if (request.Bcc is { Count: > 0 })
        {
            message.Bcc.AddRange(request.Bcc.Select(MailboxAddress.Parse));
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(request.ReplyTo));
        }

        message.Subject = request.Subject;
        var body = new BodyBuilder();

        if (request.IsHtml)
        {
            body.HtmlBody = request.Body;
        }
        else
        {
            body.TextBody = request.Body;
        }

        // Добавляем готовыми частями поверх потока вызывающего: перегрузки BodyBuilder для файлов
        // и байтов скопировали бы содержимое в память. Любое вложение делает письмо multipart/mixed.
        foreach (var attachment in attachments ?? [])
        {
            body.Attachments.Add(CreateAttachmentPart(attachment));
        }

        message.Body = body.ToMessageBody();
        return message;
    }

    private static MimePart CreateAttachmentPart(EmailAttachment attachment)
    {
        var part = new MimePart(ContentType.Parse(attachment.ContentType))
        {
            Content = new MimeContent(attachment.Content),
            ContentTransferEncoding = ContentEncoding.Base64,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = attachment.FileName
        };

        // По умолчанию MimeKit кодирует не-ASCII имена по RFC 2231, и Outlook показывает их
        // как мусор; он (как и большинство других клиентов) декодирует encoded-words RFC 2047.
        foreach (var parameter in part.ContentType.Parameters.Concat(part.ContentDisposition.Parameters))
        {
            parameter.EncodingMethod = ParameterEncodingMethod.Rfc2047;
        }

        return part;
    }

    private static FormatOptions CreateWireFormat()
    {
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;
        return format;
    }
}
