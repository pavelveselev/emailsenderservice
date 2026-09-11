using EmailSenderService.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.AuthMethods.Token;

namespace EmailSenderService.Configuration;

/// <summary>
/// Reads a KV v2 secret and projects its keys into configuration under
/// <see cref="VaultOptions.ConfigurationPrefix"/>, so a Vault key named <c>password</c>
/// binds to <c>SmtpOptions.Password</c> with no extra plumbing.
///
/// Registering this as a configuration source (rather than reading Vault from a service)
/// means the secret is layered over appsettings by the normal "last provider wins" rule,
/// and rotated values reach anything holding an <c>IOptionsMonitor</c>.
/// </summary>
internal sealed class VaultConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly VaultOptions _options;
    private readonly Timer? _reloadTimer;
    private bool _disposed;

    public VaultConfigurationProvider(VaultOptions options)
    {
        _options = options;

        if (options.ReloadInterval > TimeSpan.Zero)
        {
            _reloadTimer = new Timer(_ => Reload(), state: null, options.ReloadInterval, options.ReloadInterval);
        }
    }

    /// <summary>
    /// Blocking by necessity: <see cref="ConfigurationProvider"/> has no async contract, and the
    /// host builds configuration synchronously before the request pipeline exists.
    /// </summary>
    public override void Load() => LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    private async Task LoadAsync()
    {
        try
        {
            var client = new VaultClient(new VaultClientSettings(_options.Address, CreateAuthMethod(_options)));

            var secret = await client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: _options.SecretPath, mountPoint: _options.MountPoint);

            var prefix = string.IsNullOrWhiteSpace(_options.ConfigurationPrefix)
                ? string.Empty
                : _options.ConfigurationPrefix + ConfigurationPath.KeyDelimiter;

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in secret.Data.Data)
            {
                data[prefix + key] = value?.ToString();
            }

            Data = data;
        }
        catch (Exception ex) when (_options.Optional)
        {
            // Startup continues on the appsettings values; the readiness probe is what should
            // keep an unconfigured pod out of the Service.
            VaultConfigurationDiagnostics.ReportLoadFailure(ex, _options);
        }
        catch (Exception ex)
        {
            // The raw VaultSharp error is an opaque {"errors":[]} body; an operator staring at
            // CrashLoopBackOff needs to know which secret, which server, and which auth method.
            throw new InvalidOperationException(
                $"Could not read Vault secret '{_options.MountPoint}/{_options.SecretPath}' from " +
                $"{_options.Address} using {_options.AuthMethod} auth. Check the path, the role binding, " +
                $"and the KV version, or set {VaultOptions.SectionName}:Optional=true to start without it.", ex);
        }
    }

    private void Reload()
    {
        try
        {
            Load();
            OnReload();
        }
        catch (Exception ex)
        {
            // A failed refresh must never take the process down - keep serving the last good secret.
            VaultConfigurationDiagnostics.ReportReloadFailure(ex, _options);
        }
    }

    private static IAuthMethodInfo CreateAuthMethod(VaultOptions options) => options.AuthMethod switch
    {
        VaultAuthMethod.Kubernetes => new KubernetesAuthMethodInfo(
            mountPoint: options.KubernetesAuthMountPoint,
            roleName: options.KubernetesRole,
            jwt: File.ReadAllText(options.ServiceAccountTokenPath)),

        VaultAuthMethod.Token => new TokenAuthMethodInfo(
            options.Token ?? throw new InvalidOperationException(
                "Vault:Token is required when Vault:AuthMethod is Token.")),

        _ => throw new InvalidOperationException($"Unsupported Vault auth method '{options.AuthMethod}'.")
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reloadTimer?.Dispose();
    }
}
