using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Options;

/// <summary>
/// Limits for files sent through the multipart action. They exist so that an oversized or
/// unwanted upload is refused here with a 400 instead of reaching the relay; the relay's own
/// size cap is mirrored separately by <see cref="SmtpOptions.MaxMessageBytes"/>.
/// </summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>
    /// Request body cap for the multipart action: attachments, multipart framing and the text
    /// fields. It is a constant because <c>[RequestSizeLimit]</c> and <c>[RequestFormLimits]</c>
    /// take compile-time values; start-up validation keeps the configurable limits below it.
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
    /// Raw bytes across all files. Base64 with line breaks grows content by about 1.37x, so
    /// 20 MiB becomes roughly 28.7 MB of the relay's 30,000,000-byte message cap.
    /// </summary>
    [Range(1L, MultipartRequestBytes)]
    public long MaxTotalBytes { get; set; } = 20 * 1024 * 1024;

    [Range(1L, MultipartRequestBytes)]
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Extensions a file name may end with. Null means <see cref="DefaultAllowedExtensions"/>;
    /// a configured list replaces the defaults rather than extending them. (A pre-filled list
    /// would be appended to by the configuration binder, hence null instead.)
    /// </summary>
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    public IReadOnlySet<string> EffectiveAllowedExtensions =>
        (AllowedExtensions is { Count: > 0 } ? AllowedExtensions : DefaultAllowedExtensions)
        .Where(extension => !string.IsNullOrWhiteSpace(extension))
        .Select(extension => extension.Trim())
        .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
