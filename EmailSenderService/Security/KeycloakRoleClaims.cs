using System.Security.Claims;
using System.Text.Json;

namespace EmailSenderService.Security;

/// <summary>
/// Keycloak передаёт роли во вложенных JSON-claims <c>realm_access</c> и <c>resource_access</c>,
/// которые JWT-обработчик копирует как есть, строками. ASP.NET Core такую структуру не понимает,
/// поэтому <c>[Authorize(Roles = ...)]</c> и <c>RequireRole</c> не видят ни одной роли, пока они
/// не развёрнуты в отдельные claims <see cref="ClaimTypes.Role"/>.
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
    /// Разбирает <c>{"roles":[...]}</c>, при необходимости вложенный на один уровень под
    /// <paramref name="path"/> как <c>{"client":{"roles":[...]}}</c>.
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
            // Повреждённый claim означает отсутствие ролей, а не провал аутентификации.
            return [];
        }
    }
}
