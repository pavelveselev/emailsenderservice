using EmailSenderService.Models;
using EmailSenderService.Options;
using EmailSenderService.Security;
using EmailSenderService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmailSenderService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.SendEmail)]
[Produces("application/json")]
public sealed class EmailController(
    IEmailSender emailSender,
    AttachmentValidator attachmentValidator,
    ILogger<EmailController> logger) : ControllerBase
{
    /// <summary>Отправляет одно письмо через настроенный релей.</summary>
    /// <remarks>
    /// Для обычного письма отправьте JSON; для письма с вложениями - multipart/form-data с теми
    /// же полями (списки - повторяющимися ключами) и файловым полем <c>Attachments</c>.
    /// </remarks>
    /// <response code="200">Релей принял письмо.</response>
    /// <response code="400">Запрос не прошёл валидацию.</response>
    /// <response code="401">Токен Keycloak отсутствует или недействителен.</response>
    /// <response code="403">Токен действителен, но у него нет нужной роли.</response>
    /// <response code="413">Тело запроса или собранное из него письмо превышает допустимый размер.</response>
    /// <response code="502">Релей отклонил письмо или был недоступен.</response>
    [HttpPost("send")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SendEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public Task<ActionResult<SendEmailResponse>> Send(
        [FromBody] SendEmailRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(request, [], cancellationToken);

    /// <summary>Отправляет одно письмо с файловыми вложениями (multipart/form-data, те же поля, что и в JSON).</summary>
    /// <response code="200">Релей принял письмо.</response>
    /// <response code="400">Запрос или вложение не прошли валидацию.</response>
    /// <response code="401">Токен Keycloak отсутствует или недействителен.</response>
    /// <response code="403">Токен действителен, но у него нет нужной роли.</response>
    /// <response code="413">Тело запроса или собранное из него письмо превышает допустимый размер.</response>
    /// <response code="502">Релей отклонил письмо или был недоступен.</response>
    [HttpPost("send")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AttachmentOptions.MultipartRequestBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = AttachmentOptions.MultipartRequestBytes,
        ValueLengthLimit = (int)AttachmentOptions.MultipartRequestBytes)]
    [MultipartFormReadFilter]
    [ProducesResponseType(typeof(SendEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SendEmailResponse>> SendWithAttachments(
        [FromForm] SendEmailFormRequest request,
        CancellationToken cancellationToken)
    {
        if (!attachmentValidator.TryValidate(request.Attachments, out var validated, out var errors))
        {
            foreach (var (key, messages) in errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(key, message);
                }
            }

            return ValidationProblem(ModelState);
        }

        // Каждый поток читает буферизованную загрузку (из памяти или временного файла в /tmp)
        // без копирования. Потоки закрываются здесь, после отправки, а временные файлы
        // фреймворк удаляет по завершении ответа.
        var attachments = validated
            .Select(file => new EmailAttachment(file.FileName, file.ContentType, file.File.OpenReadStream()))
            .ToList();

        try
        {
            return await SendAsync(request, attachments, cancellationToken);
        }
        finally
        {
            foreach (var attachment in attachments)
            {
                await attachment.Content.DisposeAsync();
            }
        }
    }

    /// <summary>Единый путь к отправителю для обоих действий, чтобы они не могли разойтись.</summary>
    private async Task<ActionResult<SendEmailResponse>> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        // Имена файлов в лог не пишем: в них часто бывают персональные данные.
        logger.LogInformation(
            "Send request from {Caller} for {RecipientCount} recipient(s) with {AttachmentCount} attachment(s), {AttachmentBytes} bytes",
            User.Identity?.Name ?? "unknown", request.To.Count, attachments.Count,
            attachments.Sum(attachment => attachment.Content.Length));

        var response = await emailSender.SendAsync(request, attachments.Count == 0 ? null : attachments, cancellationToken);
        return Ok(response);
    }
}
