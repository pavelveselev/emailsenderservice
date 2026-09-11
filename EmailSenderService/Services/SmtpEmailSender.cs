using System.Net.Sockets;
using EmailSenderService.Models;
using EmailSenderService.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace EmailSenderService.Services;

/// <summary>
/// Sends through MailKit. A fresh <see cref="SmtpClient"/> is created per message on purpose:
/// MailKit's client is not thread-safe, so a shared instance would have to be locked, which
/// would serialise every request against a single connection.
/// </summary>
public sealed class SmtpEmailSender(
    IOptionsMonitor<SmtpOptions> optionsMonitor,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<SendEmailResponse> SendAsync(SendEmailRequest request, CancellationToken cancellationToken)
    {
        // Read through the monitor on every send so credentials rotated in Vault take effect
        // without a restart.
        var options = optionsMonitor.CurrentValue;
        var message = BuildMessage(request, options);

        using var client = new SmtpClient
        {
            Timeout = (int)TimeSpan.FromSeconds(options.TimeoutSeconds).TotalMilliseconds
        };

        if (options.AcceptInvalidCertificates)
        {
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        try
        {
            await client.ConnectAsync(options.Host, options.Port, options.Security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(options.UserName))
            {
                // Some relays advertise XOAUTH2 and then fail the handshake for password auth.
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            var recipientCount = message.To.Count + message.Cc.Count + message.Bcc.Count;
            logger.LogInformation(
                "Sent {MessageId} to {RecipientCount} recipient(s) via {Host}:{Port}. Relay response: {Response}",
                message.MessageId, recipientCount, options.Host, options.Port, response);

            return new SendEmailResponse(message.MessageId, recipientCount, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException ex)
        {
            logger.LogError(ex, "SMTP authentication failed for {UserName} at {Host}:{Port}",
                options.UserName, options.Host, options.Port);
            throw new EmailDeliveryException("The relay rejected the configured SMTP credentials.", ex);
        }
        catch (SmtpCommandException ex)
        {
            logger.LogError(ex, "Relay rejected the message ({ErrorCode}, mailbox {Mailbox})", ex.ErrorCode, ex.Mailbox);
            throw new EmailDeliveryException($"The relay rejected the message: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is SmtpProtocolException or SslHandshakeException or IOException
                                      or SocketException or TimeoutException)
        {
            logger.LogError(ex, "Could not reach the relay at {Host}:{Port}", options.Host, options.Port);
            throw new EmailDeliveryException("The relay could not be reached.", ex);
        }
    }

    private static MimeMessage BuildMessage(SendEmailRequest request, SmtpOptions options)
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

        message.Body = body.ToMessageBody();
        return message;
    }
}
