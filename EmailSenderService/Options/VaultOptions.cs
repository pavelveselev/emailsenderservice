namespace EmailSenderService.Options;

public enum VaultAuthMethod
{
    /// <summary>In-cluster auth using the pod's projected service account token.</summary>
    Kubernetes,

    /// <summary>Static token; for local development against `vault server -dev`.</summary>
    Token
}

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public bool Enabled { get; set; }

    public string Address { get; set; } = "http://vault:8200";

    public VaultAuthMethod AuthMethod { get; set; } = VaultAuthMethod.Kubernetes;

    /// <summary>Used when <see cref="AuthMethod"/> is Token. Supply via VAULT_TOKEN, never appsettings.</summary>
    public string? Token { get; set; }

    /// <summary>Vault role bound to this workload's Kubernetes service account.</summary>
    public string KubernetesRole { get; set; } = "email-sender";

    public string KubernetesAuthMountPoint { get; set; } = "kubernetes";

    public string ServiceAccountTokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    /// <summary>KV v2 mount point.</summary>
    public string MountPoint { get; set; } = "secret";

    /// <summary>Path within the mount, e.g. email-sender/smtp.</summary>
    public string SecretPath { get; set; } = "email-sender/smtp";

    /// <summary>Configuration section the secret's keys are written into.</summary>
    public string ConfigurationPrefix { get; set; } = SmtpOptions.SectionName;

    /// <summary>When true a Vault outage at startup logs a warning instead of failing the host.</summary>
    public bool Optional { get; set; }

    /// <summary>Re-read the secret on this interval so rotated credentials are picked up. Zero disables polling.</summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromMinutes(5);
}
