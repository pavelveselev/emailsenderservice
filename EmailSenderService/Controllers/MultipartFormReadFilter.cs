using EmailSenderService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EmailSenderService.Controllers;

/// <summary>
/// Читает multipart-форму до привязки модели, чтобы форма, нарушающая лимит, давала 413, а
/// повреждённая - 400. Если оставить это привязке модели, оба случая превращаются в 500.
/// Выполняется после фильтров <c>[RequestSizeLimit]</c> / <c>[RequestFormLimits]</c> (order 900),
/// чтобы их лимиты действовали; затем привязка модели берёт форму, закешированную в запросе.
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
            // Читатель формы сообщает и о превышении лимита, и о нарушенной разметке через
            // InvalidDataException; различить их можно только по тексту сообщения.
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
