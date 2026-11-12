using System.Globalization;
using EmailSenderService.Models;
using EmailSenderService.Options;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace EmailSenderService.Services;

/// <summary>An uploaded file that passed validation, with the name and content type this service decided on.</summary>
public sealed record ValidatedAttachment(IFormFile File, string FileName, string ContentType);

/// <summary>
/// Applies <see cref="AttachmentOptions"/> to uploaded files. Nothing about a file is taken on
/// trust: the name is reduced to a bare, printable file name and the content type is derived
/// from its extension, because both end up in headers that recipients' mail clients act on.
/// </summary>
public sealed class AttachmentValidator(IOptionsMonitor<AttachmentOptions> optionsMonitor)
{
    public const string FieldName = nameof(SendEmailFormRequest.Attachments);

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Validates <paramref name="files"/>; on failure <paramref name="errors"/> holds one message per problem, keyed by field.</summary>
    public bool TryValidate(
        IReadOnlyList<IFormFile>? files,
        out IReadOnlyList<ValidatedAttachment> attachments,
        out IDictionary<string, string[]> errors)
    {
        var options = optionsMonitor.CurrentValue;
        var allowedExtensions = options.EffectiveAllowedExtensions;
        var problems = new Dictionary<string, List<string>>();
        var validated = new List<ValidatedAttachment>();

        void Add(string key, string message)
        {
            if (!problems.TryGetValue(key, out var list))
            {
                problems[key] = list = [];
            }

            list.Add(message);
        }

        files ??= [];

        if (files.Count > options.MaxCount)
        {
            Add(FieldName, $"At most {options.MaxCount} attachments are allowed; {files.Count} were sent.");
        }

        var totalBytes = files.Sum(file => file.Length);
        if (totalBytes > options.MaxTotalBytes)
        {
            Add(FieldName, string.Create(CultureInfo.InvariantCulture, $"Attachments total {totalBytes:N0} bytes; at most {options.MaxTotalBytes:N0} are allowed."));
        }

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var key = $"{FieldName}[{i}]";
            var fileName = SanitizeFileName(file.FileName);

            if (fileName is null)
            {
                Add(key, "The file name is missing or contains no usable characters.");
            }
            else if (!allowedExtensions.Contains(Path.GetExtension(fileName)))
            {
                var extension = Path.GetExtension(fileName);
                Add(key, $"'{fileName}': files of type '{(extension.Length == 0 ? "(none)" : extension)}' are not allowed. "
                         + $"Allowed: {string.Join(", ", allowedExtensions.Order(StringComparer.OrdinalIgnoreCase))}.");
            }

            if (file.Length == 0)
            {
                Add(key, $"'{fileName ?? "(unnamed)"}' is empty.");
            }
            else if (file.Length > options.MaxFileBytes)
            {
                Add(key, string.Create(CultureInfo.InvariantCulture, $"'{fileName ?? "(unnamed)"}' is {file.Length:N0} bytes; at most {options.MaxFileBytes:N0} are allowed per file."));
            }

            if (fileName is not null)
            {
                validated.Add(new ValidatedAttachment(file, fileName, GetContentType(fileName)));
            }
        }

        errors = problems.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        attachments = errors.Count == 0 ? validated : [];
        return errors.Count == 0;
    }

    /// <summary>
    /// Reduces a client-supplied name to its last path segment with control characters removed
    /// and the length capped (keeping the extension). Returns null when nothing usable is left.
    /// </summary>
    public static string? SanitizeFileName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        // Path.GetFileName only splits on '/' on Linux, and Windows clients send "C:\dir\file".
        var name = Path.GetFileName(rawName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length == 0 || name is "." or "..")
        {
            return null;
        }

        if (name.Length > AttachmentOptions.MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length >= AttachmentOptions.MaxFileNameLength)
            {
                extension = string.Empty;
            }

            name = name[..(AttachmentOptions.MaxFileNameLength - extension.Length)].TrimEnd() + extension;
        }

        return name;
    }

    private static string GetContentType(string fileName) =>
        ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
}
