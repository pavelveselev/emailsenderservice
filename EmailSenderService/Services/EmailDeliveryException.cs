namespace EmailSenderService.Services;

/// <summary>Выбрасывается, когда вышестоящий релей отклонил письмо или недоступен; отдаётся как HTTP 502.</summary>
public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
