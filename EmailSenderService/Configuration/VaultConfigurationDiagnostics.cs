using EmailSenderService.Options;

namespace EmailSenderService.Configuration;

/// <summary>
/// Configuration providers run before the DI container exists, so failures cannot go through
/// <c>ILogger</c>. Messages are buffered here and replayed once logging is available.
/// </summary>
internal static class VaultConfigurationDiagnostics
{
    private static readonly List<(string Message, Exception Exception)> Buffer = [];
    private static readonly Lock Gate = new();

    public static void ReportLoadFailure(Exception exception, VaultOptions options) => Add(
        $"Could not read Vault secret {options.MountPoint}/{options.SecretPath} from {options.Address}; " +
        "continuing with configuration file values because Vault:Optional is true.", exception);

    public static void ReportReloadFailure(Exception exception, VaultOptions options) => Add(
        $"Scheduled refresh of Vault secret {options.MountPoint}/{options.SecretPath} failed; " +
        "keeping the previously loaded values.", exception);

    private static void Add(string message, Exception exception)
    {
        lock (Gate)
        {
            Buffer.Add((message, exception));
        }
    }

    /// <summary>Writes and clears anything buffered during host construction.</summary>
    public static void Drain(ILogger logger)
    {
        lock (Gate)
        {
            foreach (var (message, exception) in Buffer)
            {
                logger.LogWarning(exception, "{Message}", message);
            }

            Buffer.Clear();
        }
    }
}
