namespace EmailSenderService.Models;

/// <summary>
/// Вариант <see cref="SendEmailRequest"/> для multipart/form-data: те же поля (списки -
/// повторяющимися ключами, например <c>to=a@x</c> дважды) с той же валидацией, плюс файлы.
/// </summary>
public sealed class SendEmailFormRequest : SendEmailRequest
{
    /// <summary>Ноль или больше файлов. Тип содержимого определяется по расширению файла; присланный клиентом игнорируется.</summary>
    public IReadOnlyList<IFormFile>? Attachments { get; init; }
}
