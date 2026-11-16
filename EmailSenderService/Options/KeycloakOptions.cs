using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Options;

/// <summary>
/// Настройки проверки токенов для межсервисных вызовов. Вызывающие сами получают токен в
/// Keycloak через client credentials grant; этот сервис только проверяет его.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>URL издателя (realm), например https://keycloak.example.com/realms/corp.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Ожидаемый claim <c>aud</c>. Keycloak не включает клиента в <c>aud</c>, если у вызывающего
    /// клиента не настроен audience mapper, поэтому до его появления может понадобиться
    /// <see cref="ValidateAudience"/> = false.
    /// </summary>
    public string Audience { get; set; } = "email-sender";

    public bool ValidateAudience { get; set; } = true;

    /// <summary>Отключайте только для Keycloak по обычному HTTP при локальной разработке.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Роль realm или клиента, которая нужна сервисному аккаунту вызывающего для отправки почты.</summary>
    public string RequiredRole { get; set; } = "email-sender";

    /// <summary>Client id, чьи роли из <c>resource_access</c> превращаются в claims ролей.</summary>
    public string? RolesClientId { get; set; }
}
