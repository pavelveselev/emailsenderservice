using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EmailSenderService.Services;

/// <summary>
/// Превращает сбой релея в 502 с телом ProblemDetails. Вызывающий ни в чём не виноват, поэтому
/// это не должно выглядеть как 500, а сообщение самого релея передаётся дальше, потому что
/// обычно в нём назван отклонённый ящик.
/// </summary>
public sealed class EmailDeliveryExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not EmailDeliveryException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "The message could not be delivered.",
                Detail = exception.Message,
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.3"
            }
        });
    }
}
