using EmailSenderService.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailSenderService.Health;

/// <summary>
/// Readiness gate. Deliberately does not open an SMTP connection - probes run every few seconds
/// and a relay would treat that as abuse. It answers the question the pod actually needs answered:
/// did the full configuration arrive, or are we running on an incomplete overlay?
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

        // A user name with no password almost always means the credential Secret was not
        // projected into the pod; better to stay out of the Service than to fail every send.
        if (!string.IsNullOrWhiteSpace(smtp.UserName) && string.IsNullOrWhiteSpace(smtp.Password))
        {
            missing.Add($"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Password)} (expected from the credential Secret)");
        }

        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy($"SMTP configured for {smtp.Host}:{smtp.Port}.")
            : HealthCheckResult.Unhealthy($"Missing configuration: {string.Join(", ", missing)}."));
    }
}
