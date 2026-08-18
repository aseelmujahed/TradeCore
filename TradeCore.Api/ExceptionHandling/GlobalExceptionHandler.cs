using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace TradeCore.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isBusinessRuleFailure = exception is InvalidOperationException;

        if (isBusinessRuleFailure)
        {
            logger.LogWarning(exception, "A business rule prevented the requested operation.");
        }
        else
        {
            logger.LogError(exception, "An unhandled exception occurred while processing the request.");
        }

        var problemDetails = new ProblemDetails
        {
            Status = isBusinessRuleFailure
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError,
            Title = isBusinessRuleFailure
                ? "Invalid operation."
                : "An unexpected error occurred.",
            Detail = isBusinessRuleFailure
                ? exception.Message
                : "An internal server error occurred.",
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        httpContext.Response.ContentType = "application/problem+json";

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken: cancellationToken);

        return true;
    }
}
