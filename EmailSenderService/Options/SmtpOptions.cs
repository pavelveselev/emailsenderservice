using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace EmailSenderService.Options;

/// <summary>
/// Настройки SMTP-транспорта. Host/port/security обычно приходят из appsettings или Helm
/// ConfigMap; <see cref="UserName"/> и <see cref="Password"/> должны приходить как переменные
/// окружения из Kubernetes Secret и никогда - из ConfigMap.
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

    /// <summary>Адрес отправителя. Клиенты не могут его переопределить - в этом и смысл сервиса.</summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    public string? FromDisplayName { get; set; }

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Максимальный размер письма, принимаемый релеем, в том виде, в каком оно уходит в сеть.
    /// Собранное письмо сверяется с ним до подключения, поэтому слишком большое письмо даёт 413
    /// от этого API, а не 502 от релея. Держите значение в соответствии с лимитом SIZE релея.
    /// </summary>
    [Range(1L, long.MaxValue)]
    public long MaxMessageBytes { get; set; } = 30_000_000;

    /// <summary>Принимать недоверенные сертификаты сервера. Только для тестовых релеев.</summary>
    public bool AcceptInvalidCertificates { get; set; }
}
