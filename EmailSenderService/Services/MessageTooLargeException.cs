using System.Globalization;

namespace EmailSenderService.Services;

/// <summary>
/// The built message exceeds <c>Smtp:MaxMessageBytes</c>; surfaced as HTTP 413. Raised before
/// any connection is made: the caller's input is too big, the relay is fine.
/// </summary>
public sealed class MessageTooLargeException(long messageBytes, long limitBytes)
    : Exception(string.Create(CultureInfo.InvariantCulture,
        $"The encoded message is {messageBytes:N0} bytes; the relay accepts at most {limitBytes:N0}. Attachments grow by about a third when encoded, so reduce them accordingly."))
{
    public long MessageBytes { get; } = messageBytes;

    public long LimitBytes { get; } = limitBytes;
}
