using EmailSenderService.Options;

namespace EmailSenderService.Configuration;

internal sealed class VaultConfigurationSource(VaultOptions options) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new VaultConfigurationProvider(options);
}

public static class VaultConfigurationExtensions
{
    /// <summary>
    /// Adds Vault as the highest-priority configuration source when <c>Vault:Enabled</c> is true.
    /// Bound from the configuration built so far, so VAULT_* environment variables and appsettings
    /// both work for pointing at the server.
    /// </summary>
    public static IConfigurationBuilder AddVaultSecrets(this IConfigurationManager configuration)
    {
        var options = configuration.GetSection(VaultOptions.SectionName).Get<VaultOptions>() ?? new VaultOptions();

        if (!options.Enabled)
        {
            return configuration;
        }

        return configuration.Add(new VaultConfigurationSource(options));
    }
}
