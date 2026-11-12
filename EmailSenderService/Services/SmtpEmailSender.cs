using EmailSenderService.Models;
using EmailSenderService.Options;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.IO;
using MimeKit.Utils;

namespace EmailSenderService.Services;

/// <summary>
/// Builds the <see cref="MimeMessage"/>, checks it against the relay's size limit and hands it
/// to <see cref="ISmtpTransport"/>. This API is the only place that limit is enforced before
/// the relay itself, so the check runs on the fully encoded message, not on an estimate.
/// </summary>
public sealed class SmtpEmailSender(
    IOptionsMonitor<SmtpOptions> optionsMonitor,
    ISmtpTransport transport,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    // SMTP puts CRLF on the wire; FormatOptions.Default uses the platform newline, which on
    // Linux would under-count every line of base64 by one byte.
    private static readonly FormatOptions WireFormat = CreateWireFormat();

    public async Task<SendEmailResponse> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        // Read through the monitor on every send so a rotated credential takes effect as soon
        // as the configuration source behind it reloads.
        var options = optionsMonitor.CurrentValue;

        // Not disposed: disposing the message would close the attachment streams, which belong
        // to the caller and are released by it once this method returns.
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
    /// Fixes every part's transfer encoding for the strictest (7-bit) transport first, so the
    /// size measured here is what goes on the wire - MailKit's own Prepare leaves explicitly
    /// encoded parts alone. Bcc is still written here, so the result errs slightly high.
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

        // Added as ready-made parts over the caller's stream: BodyBuilder's file and byte
        // overloads would copy the content into memory. Any attachment makes it multipart/mixed.
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

        // MimeKit encodes non-ASCII names per RFC 2231 by default, which Outlook shows as
        // garbage; RFC 2047 encoded-words are what it (and most other clients) decode.
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
