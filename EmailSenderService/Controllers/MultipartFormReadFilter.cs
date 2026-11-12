using EmailSenderService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EmailSenderService.Controllers;

/// <summary>
/// Reads the multipart form before model binding so a form that breaks a limit becomes a 413
/// and a malformed one a 400. Left to model binding, both surface as a 500. Runs after the
/// <c>[RequestSizeLimit]</c> / <c>[RequestFormLimits]</c> filters (order 900) so their limits
/// are in force; model binding then reuses the form cached on the request.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MultipartFormReadFilterAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => 1000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (request.HasFormContentType)
        {
            int? failureStatus = null;
            string? detail = null;

            try
            {
                await request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            catch (BadHttpRequestException ex)
            {
                failureStatus = ex.StatusCode;
                detail = ex.Message;
            }
            // The form reader reports both broken limits and broken framing as
            // InvalidDataException; only the message tells them apart.
            catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
            {
                failureStatus = StatusCodes.Status413PayloadTooLarge;
                detail = ex.Message;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException
                                          && !context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                failureStatus = StatusCodes.Status400BadRequest;
                detail = $"The multipart body could not be read: {ex.Message}";
            }

            if (failureStatus is { } status)
            {
                var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
                var problem = factory.CreateProblemDetails(
                    context.HttpContext,
                    status,
                    title: status == StatusCodes.Status413PayloadTooLarge
                        ? "The request is too large."
                        : "The request body is malformed.",
                    type: status == StatusCodes.Status413PayloadTooLarge
                        ? PayloadTooLargeExceptionHandler.ProblemType
                        : null,
                    detail: detail);

                context.Result = new ObjectResult(problem)
                {
                    StatusCode = status,
                    ContentTypes = { "application/problem+json" }
                };
                return;
            }
        }

        await next();
    }
}
