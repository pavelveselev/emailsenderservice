using EmailSenderService.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailSenderService.Health;

/// <summary>
/// Проверка готовности. Намеренно не открывает SMTP-соединение - пробы идут каждые несколько
/// секунд, и релей счёл бы это злоупотреблением. Она отвечает на вопрос, действительно важный
/// для пода: пришла ли конфигурация целиком или мы работаем на неполном наборе настроек?
/// </summary>
public sealed class SmtpConfigurationHealthCheck(IOptionsMonitor<SmtpOptions> optionsMonitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var smtp = optionsMonitor.CurrentValue;
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            missing.Add($"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Host)}");
        }

        if (string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            missing.Add($"{SmtpOptions.SectionName}:{nameof(SmtpOptions.FromAddress)}");
        }

        // Имя пользователя без пароля почти всегда значит, что Secret с учётными данными не попал
        // в под; лучше остаться вне Service, чем проваливать каждую отправку.
        if (!string.IsNullOrWhiteSpace(smtp.UserName) && string.IsNullOrWhiteSpace(smtp.Password))
        {
            missing.Add($"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Password)} (expected from the credential Secret)");
        }

        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy($"SMTP configured for {smtp.Host}:{smtp.Port}.")
            : HealthCheckResult.Unhealthy($"Missing configuration: {string.Join(", ", missing)}."));
    }
}
