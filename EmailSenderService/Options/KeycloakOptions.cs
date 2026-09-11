using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Options;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm issuer URL, e.g. https://keycloak.example.com/realms/corp.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Expected <c>aud</c> claim. Keycloak omits the client from <c>aud</c> unless an
    /// audience mapper is configured on the client, so <see cref="ValidateAudience"/>
    /// may need to be false until that mapper exists.
    /// </summary>
    public string Audience { get; set; } = "email-sender";

    public bool ValidateAudience { get; set; } = true;

    /// <summary>Set false only for plain-HTTP Keycloak in local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Realm or client role a caller must hold to send mail.</summary>
    public string RequiredRole { get; set; } = "email-sender";

    /// <summary>Client id whose <c>resource_access</c> roles are promoted to role claims.</summary>
    public string? RolesClientId { get; set; }

    /// <summary>Public client id the Swagger UI authorizes with (authorization code + PKCE).</summary>
    public string? SwaggerClientId { get; set; }

    public string AuthorizationUrl => $"{Authority.TrimEnd('/')}/protocol/openid-connect/auth";

    public string TokenUrl => $"{Authority.TrimEnd('/')}/protocol/openid-connect/token";
}
