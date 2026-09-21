using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Options;

/// <summary>
/// Token validation settings for service-to-service calls. Callers obtain a token from
/// Keycloak's client credentials grant themselves; this service only validates it.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm issuer URL, e.g. https://keycloak.example.com/realms/corp.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Expected <c>aud</c> claim. Keycloak omits the client from <c>aud</c> unless an
    /// audience mapper is configured on the calling client, so <see cref="ValidateAudience"/>
    /// may need to be false until that mapper exists.
    /// </summary>
    public string Audience { get; set; } = "email-sender";

    public bool ValidateAudience { get; set; } = true;

    /// <summary>Set false only for plain-HTTP Keycloak in local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Realm or client role a calling service account must hold to send mail.</summary>
    public string RequiredRole { get; set; } = "email-sender";

    /// <summary>Client id whose <c>resource_access</c> roles are promoted to role claims.</summary>
    public string? RolesClientId { get; set; }
}
