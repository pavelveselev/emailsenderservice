using System.Globalization;

namespace EmailSenderService.Services;

/// <summary>
/// Собранное письмо превышает <c>Smtp:MaxMessageBytes</c>; отдаётся как HTTP 413. Выбрасывается
/// до какого-либо подключения: слишком велики входные данные вызывающего, с релеем всё в порядке.
/// </summary>
public sealed class MessageTooLargeException(long messageBytes, long limitBytes)
    : Exception(string.Create(CultureInfo.InvariantCulture,
        $"The encoded message is {messageBytes:N0} bytes; the relay accepts at most {limitBytes:N0}. Attachments grow by about a third when encoded, so reduce them accordingly."))
{
    public long MessageBytes { get; } = messageBytes;

    public long LimitBytes { get; } = limitBytes;
}
