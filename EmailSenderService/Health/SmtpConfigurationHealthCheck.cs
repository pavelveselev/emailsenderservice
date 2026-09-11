using EmailSenderService.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailSenderService.Health;

/// <summary>
/// Readiness gate. Deliberately does not open an SMTP connection - probes run every few seconds
/// and a relay would treat that as abuse. It answers the question the pod actually needs answered:
/// did the Vault overlay arrive, or are we running on an incomplete configuration?
/// </summary>
public sealed class SmtpConfigurationHealthCheck(
    IOptionsMonitor<SmtpOptions> optionsMonitor,
    IOptionsMonitor<VaultOptions> vaultOptionsMonitor) : IHealthCheck
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

        // With Vault enabled the credentials are the whole reason this service exists; an empty
        // password here almost always means the secret never loaded.
        if (vaultOptionsMonitor.CurrentValue.Enabled
            && !string.IsNullOrWhiteSpace(smtp.UserName)
            && string.IsNullOrWhiteSpace(smtp.Password))
        {
            missing.Add($"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Password)} (expected from Vault)");
        }

        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy($"SMTP configured for {smtp.Host}:{smtp.Port}.")
            : HealthCheckResult.Unhealthy($"Missing configuration: {string.Join(", ", missing)}."));
    }
}
