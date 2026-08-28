using Microsoft.AspNetCore.Mvc;

namespace CountryPackage.Api.Services;

public sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            await WriteProblemAsync(context, exception.StatusCode, exception.Code, exception.Title, exception.Detail);
        }
        catch (BadHttpRequestException exception)
        {
            await WriteProblemAsync(context, exception.StatusCode, "request.invalid", "The request is invalid.", exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure for trace {TraceId}", context.TraceIdentifier);
            await WriteProblemAsync(context, 500, "server.error", "An unexpected error occurred.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string code, string title, string? detail)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier
            }
        };
        await context.Response.WriteAsJsonAsync(problem, JsonDefaults.Options);
    }
}
