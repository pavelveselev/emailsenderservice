using EmailSenderService.Models;
using EmailSenderService.Security;
using EmailSenderService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmailSenderService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.SendEmail)]
[Produces("application/json")]
public sealed class EmailController(IEmailSender emailSender, ILogger<EmailController> logger) : ControllerBase
{
    /// <summary>Sends one message through the configured relay.</summary>
    /// <response code="200">The relay accepted the message.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="401">No or invalid Keycloak token.</response>
    /// <response code="403">Token is valid but lacks the required role.</response>
    /// <response code="502">The relay rejected the message or was unreachable.</response>
    [HttpPost("send")]
    [ProducesResponseType(typeof(SendEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SendEmailResponse>> Send(
        [FromBody] SendEmailRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Send request from {Caller} for {RecipientCount} recipient(s)",
            User.Identity?.Name ?? "unknown", request.To.Count);

        var response = await emailSender.SendAsync(request, cancellationToken);
        return Ok(response);
    }
}
