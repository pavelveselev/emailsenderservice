using System.Collections;
using System.ComponentModel.DataAnnotations;
using MimeKit;

namespace EmailSenderService.Models;

/// <summary>
/// Validates every entry of a string collection with MimeKit's parser rather than a regex,
/// so what passes validation is exactly what the SMTP layer can later put on the envelope.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EmailAddressListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not IEnumerable items || value is string)
        {
            return new ValidationResult($"{context.MemberName} must be a list of email addresses.");
        }

        var index = 0;
        foreach (var item in items)
        {
            if (!IsRoutableAddress(item as string))
            {
                return new ValidationResult(
                    $"{context.MemberName}[{index}] is not a valid email address.",
                    context.MemberName is null ? null : [context.MemberName]);
            }

            index++;
        }

        return ValidationResult.Success;
    }

    /// <summary>
    /// MimeKit happily parses a bare local part such as "postmaster" into a valid
    /// <see cref="MailboxAddress"/>, which the relay would then reject at RCPT TO time.
    /// Requiring a non-empty domain turns that into a 400 instead of a 502.
    /// </summary>
    private static bool IsRoutableAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !MailboxAddress.TryParse(value, out var mailbox))
        {
            return false;
        }

        var separator = mailbox.Address.LastIndexOf('@');
        return separator > 0 && separator < mailbox.Address.Length - 1;
    }
}
