using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Options;

/// <summary>
/// Лимиты для файлов, отправляемых через multipart-действие. Они нужны, чтобы слишком большая
/// или нежелательная загрузка отклонялась здесь с 400, а не доходила до релея; собственный
/// лимит размера релея отдельно отражён в <see cref="SmtpOptions.MaxMessageBytes"/>.
/// </summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>
    /// Лимит тела запроса для multipart-действия: вложения, multipart-разметка и текстовые поля.
    /// Это константа, потому что <c>[RequestSizeLimit]</c> и <c>[RequestFormLimits]</c> принимают
    /// значения времени компиляции; валидация при старте держит настраиваемые лимиты ниже него.
    /// </summary>
    public const long MultipartRequestBytes = 25 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    public static readonly IReadOnlyList<string> DefaultAllowedExtensions =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".odt", ".ods",
        ".txt", ".csv", ".png", ".jpg", ".jpeg", ".zip"
    ];

    [Range(1, 100)]
    public int MaxCount { get; set; } = 10;

    /// <summary>
    /// Исходные байты всех файлов в сумме. Base64 с переносами строк увеличивает содержимое
    /// примерно в 1.37 раза, так что 20 MiB превращаются примерно в 28.7 МБ из лимита релея
    /// в 30 000 000 байт на письмо.
    /// </summary>
    [Range(1L, MultipartRequestBytes)]
    public long MaxTotalBytes { get; set; } = 20 * 1024 * 1024;

    [Range(1L, MultipartRequestBytes)]
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Допустимые расширения имени файла. Null означает <see cref="DefaultAllowedExtensions"/>;
    /// заданный в конфигурации список заменяет значения по умолчанию, а не дополняет их.
    /// (В заранее заполненный список привязчик конфигурации дописывал бы элементы, поэтому null.)
    /// </summary>
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    public IReadOnlySet<string> EffectiveAllowedExtensions =>
        (AllowedExtensions is { Count: > 0 } ? AllowedExtensions : DefaultAllowedExtensions)
        .Where(extension => !string.IsNullOrWhiteSpace(extension))
        .Select(extension => extension.Trim())
        .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
