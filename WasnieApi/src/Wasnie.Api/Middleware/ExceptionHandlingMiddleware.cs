using System.Net;
using System.Text.Json;
using FluentValidation;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, "Validation failed.",
                ex.Errors.Select(e => e.ErrorMessage));
        }
        catch (UnauthorizedAccessException)
        {
            await WriteErrorResponse(context, HttpStatusCode.Unauthorized, "Unauthorized.", null);
        }
        catch (ForbiddenException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Forbidden",
                message = ex.Message,
            }));
        }
        catch (TierLimitExceededException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "TierLimitExceeded",
                message = ex.Message,
                tier = ex.Tier,
                currentCount = ex.CurrentCount,
                limit = ex.Limit,
                upgradePath = "/account/subscription",
            }));
        }
        catch (DomainException ex)
        {
            await WriteErrorResponse(context, HttpStatusCode.UnprocessableEntity, ex.Message, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.", null);
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        HttpStatusCode statusCode,
        string message,
        IEnumerable<string>? details)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = (int)statusCode,
            message,
            details = details?.ToArray()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
