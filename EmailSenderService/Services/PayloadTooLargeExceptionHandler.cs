using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EmailSenderService.Services;

/// <summary>
/// Renders oversized input as 413 with a ProblemDetails body: a request body over Kestrel's cap,
/// or a built message over the relay's size limit. Either way the caller has to send less, so
/// this must not surface as a 500 (the default for these exceptions) or a 502 (the relay is fine).
/// </summary>
public sealed class PayloadTooLargeExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public const string ProblemType = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.14";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var detail = exception switch
        {
            MessageTooLargeException tooLarge => tooLarge.Message,
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => exception.Message,
            _ => null
        };

        if (detail is null || httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "The request is too large.",
                Detail = detail,
                Type = ProblemType
            }
        });
    }
}
