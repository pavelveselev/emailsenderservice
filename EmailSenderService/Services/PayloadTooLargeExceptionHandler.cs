using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EmailSenderService.Services;

/// <summary>
/// Отдаёт слишком большие входные данные как 413 с телом ProblemDetails: тело запроса сверх
/// лимита Kestrel или собранное письмо сверх лимита релея. В обоих случаях вызывающему нужно
/// отправить меньше, поэтому это не должно выглядеть как 500 (по умолчанию для этих исключений)
/// или 502 (с релеем всё в порядке).
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
