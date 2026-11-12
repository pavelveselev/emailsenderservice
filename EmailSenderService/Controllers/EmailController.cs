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
    /// <summary>Sends one message through the configured relay.</summary>
    /// <remarks>
    /// Send JSON for a plain message, or multipart/form-data with the same fields (lists as
    /// repeated keys) plus an <c>Attachments</c> file field for one with attachments.
    /// </remarks>
    /// <response code="200">The relay accepted the message.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="401">No or invalid Keycloak token.</response>
    /// <response code="403">Token is valid but lacks the required role.</response>
    /// <response code="413">The request body, or the message built from it, exceeds the size limit.</response>
    /// <response code="502">The relay rejected the message or was unreachable.</response>
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

    /// <summary>Sends one message with file attachments (multipart/form-data, same fields as the JSON form).</summary>
    /// <response code="200">The relay accepted the message.</response>
    /// <response code="400">The request or an attachment failed validation.</response>
    /// <response code="401">No or invalid Keycloak token.</response>
    /// <response code="403">Token is valid but lacks the required role.</response>
    /// <response code="413">The request body, or the message built from it, exceeds the size limit.</response>
    /// <response code="502">The relay rejected the message or was unreachable.</response>
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

        // Each stream reads the buffered upload (memory or a temp file under /tmp) without
        // copying it. They are closed here, after the send, and the framework deletes the temp
        // files when the response completes.
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

    /// <summary>The one path both actions take to the sender, so they cannot drift apart.</summary>
    private async Task<ActionResult<SendEmailResponse>> SendAsync(
        SendEmailRequest request,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        // File names stay out of the log: they routinely carry personal data.
        logger.LogInformation(
            "Send request from {Caller} for {RecipientCount} recipient(s) with {AttachmentCount} attachment(s), {AttachmentBytes} bytes",
            User.Identity?.Name ?? "unknown", request.To.Count, attachments.Count,
            attachments.Sum(attachment => attachment.Content.Length));

        var response = await emailSender.SendAsync(request, attachments.Count == 0 ? null : attachments, cancellationToken);
        return Ok(response);
    }
}
