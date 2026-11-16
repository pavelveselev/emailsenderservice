using System.Collections;
using System.ComponentModel.DataAnnotations;
using MimeKit;

namespace EmailSenderService.Models;

/// <summary>
/// Проверяет каждый элемент коллекции строк парсером MimeKit, а не регулярным выражением,
/// чтобы валидацию проходило ровно то, что SMTP-слой потом сможет поставить в конверт.
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
    /// MimeKit охотно разбирает голую локальную часть вроде "postmaster" в валидный
    /// <see cref="MailboxAddress"/>, который релей затем отклонил бы на RCPT TO.
    /// Требование непустого домена превращает это в 400 вместо 502.
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
