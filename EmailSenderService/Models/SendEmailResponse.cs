namespace EmailSenderService.Models;

/// <param name="MessageId">Message-Id header generated for the message; use it to correlate with relay logs.</param>
/// <param name="RecipientCount">Total addresses on the envelope (To + Cc + Bcc).</param>
/// <param name="SentAtUtc">When the relay accepted the message.</param>
public sealed record SendEmailResponse(string MessageId, int RecipientCount, DateTimeOffset SentAtUtc);
