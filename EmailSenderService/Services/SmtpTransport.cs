using System.Net.Sockets;
using EmailSenderService.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EmailSenderService.Services;

/// <summary>
/// Отправляет через MailKit. Новый <see cref="SmtpClient"/> на каждое письмо создаётся намеренно:
/// клиент MailKit не потокобезопасен, так что общий экземпляр пришлось бы блокировать, и все
/// запросы выстроились бы в очередь к одному соединению.
/// </summary>
public sealed class SmtpTransport(ILogger<SmtpTransport> logger) : ISmtpTransport
{
    public async Task<string> SendAsync(MimeMessage message, SmtpOptions options, CancellationToken cancellationToken)
    {
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
                // Некоторые релеи объявляют XOAUTH2, а затем проваливают рукопожатие при входе по паролю.
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return response;
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
}
