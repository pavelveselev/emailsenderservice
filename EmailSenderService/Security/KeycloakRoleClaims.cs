using System.Security.Claims;
using System.Text.Json;

namespace EmailSenderService.Security;

/// <summary>
/// Keycloak ships roles inside the nested <c>realm_access</c> and <c>resource_access</c> JSON
/// claims, which the JWT handler copies across verbatim as strings. Nothing in ASP.NET Core
/// understands that shape, so <c>[Authorize(Roles = ...)]</c> and <c>RequireRole</c> see no roles
/// at all until these are flattened into individual <see cref="ClaimTypes.Role"/> claims.
/// </summary>
public static class KeycloakRoleClaims
{
    private const string RealmAccessClaim = "realm_access";
    private const string ResourceAccessClaim = "resource_access";

    public static void Map(ClaimsPrincipal principal, string? rolesClientId)
    {
        if (principal.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var role in ReadRealmRoles(identity).Concat(ReadClientRoles(identity, rolesClientId)))
        {
            if (!principal.IsInRole(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }

    private static IEnumerable<string> ReadRealmRoles(ClaimsIdentity identity)
        => ReadRoleArray(identity.FindFirst(RealmAccessClaim)?.Value, path: null);

    private static IEnumerable<string> ReadClientRoles(ClaimsIdentity identity, string? clientId)
        => string.IsNullOrWhiteSpace(clientId)
            ? []
            : ReadRoleArray(identity.FindFirst(ResourceAccessClaim)?.Value, clientId);

    /// <summary>
    /// Parses <c>{"roles":[...]}</c>, optionally nested one level under <paramref name="path"/>
    /// as <c>{"client":{"roles":[...]}}</c>.
    /// </summary>
    private static IEnumerable<string> ReadRoleArray(string? json, string? path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;

            if (path is not null && !element.TryGetProperty(path, out element))
            {
                return [];
            }

            if (!element.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return roles.EnumerateArray()
                .Select(role => role.GetString())
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role!)
                .ToArray();
        }
        catch (JsonException)
        {
            // A malformed claim means no roles, not a failed authentication.
            return [];
        }
    }
}
