namespace EmailSenderService.Models;

/// <summary>
/// The multipart/form-data form of <see cref="SendEmailRequest"/>: the same fields (lists as
/// repeated keys, e.g. <c>to=a@x</c> twice) with the same validation, plus files.
/// </summary>
public sealed class SendEmailFormRequest : SendEmailRequest
{
    /// <summary>Zero or more files. The content type is derived from the file extension; the one the client sends is ignored.</summary>
    public IReadOnlyList<IFormFile>? Attachments { get; init; }
}
