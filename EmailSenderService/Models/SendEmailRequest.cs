using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Models;

/// <summary>
/// Письмо для доставки. Адрес отправителя задаётся конфигурацией сервера и намеренно не входит
/// в этот контракт. <see cref="SendEmailFormRequest"/> расширяет его для multipart-действия,
/// так что у обоих путей одни и те же поля и валидация.
/// </summary>
public class SendEmailRequest
{
    /// <summary>Основные получатели. Нужен хотя бы один.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one recipient is required.")]
    [EmailAddressList]
    // Изменяемый список, а не []: привязка формы копирует в существующую коллекцию и молча
    // пропускает коллекцию только для чтения (как пустой массив от []), оставляя To пустым.
    public IReadOnlyList<string> To { get; init; } = new List<string>();

    [EmailAddressList]
    public IReadOnlyList<string>? Cc { get; init; }

    [EmailAddressList]
    public IReadOnlyList<string>? Bcc { get; init; }

    /// <summary>Адрес для ответов. Необязательный.</summary>
    [EmailAddress]
    public string? ReplyTo { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(998)]
    public string Subject { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Body { get; init; } = string.Empty;

    /// <summary>Если true, тело отправляется как text/html, иначе как text/plain.</summary>
    public bool IsHtml { get; init; }
}
