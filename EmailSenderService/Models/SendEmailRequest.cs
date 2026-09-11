using System.ComponentModel.DataAnnotations;

namespace EmailSenderService.Models;

/// <summary>
/// A message to deliver. The sender address is fixed by server configuration and is
/// deliberately not part of this contract.
/// </summary>
public sealed class SendEmailRequest
{
    /// <summary>Primary recipients. At least one is required.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one recipient is required.")]
    [EmailAddressList]
    public IReadOnlyList<string> To { get; init; } = [];

    [EmailAddressList]
    public IReadOnlyList<string>? Cc { get; init; }

    [EmailAddressList]
    public IReadOnlyList<string>? Bcc { get; init; }

    /// <summary>Address replies are directed to. Optional.</summary>
    [EmailAddress]
    public string? ReplyTo { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(998)]
    public string Subject { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Body { get; init; } = string.Empty;

    /// <summary>When true the body is sent as text/html, otherwise as text/plain.</summary>
    public bool IsHtml { get; init; }
}
