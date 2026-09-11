using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace EmailSenderService.Options;

/// <summary>
/// SMTP transport settings. Host/port/security normally come from appsettings or the Helm
/// ConfigMap; <see cref="UserName"/> and <see cref="Password"/> are expected to be overlaid
/// from Vault at startup (see <c>Configuration/VaultConfigurationProvider</c>).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>Envelope sender. Clients cannot override this - that is the point of the service.</summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    public string? FromDisplayName { get; set; }

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Accept untrusted server certificates. Intended for test relays only.</summary>
    public bool AcceptInvalidCertificates { get; set; }
}
