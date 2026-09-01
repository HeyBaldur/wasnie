using System.Net;
using System.Text.Json;
using FluentValidation;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IWebHostEnvironment env)
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
        // ★ 409, and it carries a CODE the client maps to a sentence. The user was looking at a set of
        // credits that has since changed; the right client behaviour is to reload and show what is
        // there now, never to retry the same body — which is what a 400 would invite.
        catch (AccountSnapshotStaleException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "AccountSnapshotStale",
                reason = ex.Reason,
                message = ex.Message,
            }));
        }
        catch (StripeUnavailableException ex)
        {
            logger.LogWarning(ex, "Stripe API unavailable");
            await WriteErrorResponse(context, HttpStatusCode.ServiceUnavailable,
                ex.Message, null);
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
        // Same 403 as a permission denial, different discriminator: the client shows a locked control
        // with an upgrade path here, where ForbiddenException makes it hide the control entirely.
        catch (PaidPlanRequiredException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "PaidPlanRequired",
                message = ex.Message,
                feature = ex.Feature,
                tier = ex.CurrentTier,
                upgradeTier = ex.UpgradeTier,
                upgradePath = "/subscription",
            }));
        }
        // ★ 422 CARRYING A CODE AND ITS DATA, AND DELIBERATELY NO `message`. This is the same
        // refusal as the DomainException below, minus the English sentence: the client owns the
        // wording in EN, ES and PL. Sending a `message` too would be worse than sending none — every
        // caller reads that field first, so the untranslated sentence would win over the translation
        // and the code would be decoration. A client that does not know `code` therefore gets no
        // message and falls back to its own generic error, which is the honest outcome.
        //
        // Must precede the DomainException catch: DomainCodedException derives from it.
        catch (DomainCodedException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = (int)HttpStatusCode.UnprocessableEntity,
                code = ex.Code,
                parameters = ex.Parameters,
            }));
        }
        catch (DomainException ex)
        {
            await WriteErrorResponse(context, HttpStatusCode.UnprocessableEntity, ex.Message, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            // In Development: expose the actual exception so devs can diagnose without
            // hunting through logs. In Production: generic message only (security).
            var message = env.IsDevelopment()
                ? $"[{ex.GetType().Name}] {ex.Message}"
                : "An unexpected error occurred.";

            var details = env.IsDevelopment() && ex.InnerException is not null
                ? new[] { $"Inner: [{ex.InnerException.GetType().Name}] {ex.InnerException.Message}" }
                : null;

            await WriteErrorResponse(context, HttpStatusCode.InternalServerError, message, details);
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
