using System.Text.Json;
using KeyValidation.Api.DeveloperIntegration;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KeyValidation.Api;
public sealed class ApiRequestMiddleware(RequestDelegate next, bool localCookies, ILogger logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers.Pragma = "no-cache";
        }

        if (!localCookies && !context.Request.IsHttps && (context.Request.Path.StartsWithSegments("/api") || DeveloperMcpConfiguration.IsMcpRequest(context)))
        {
            await ApiErrors.Write(context, 400, "HTTPS_REQUIRED");
            return;
        }

        try
        {
            if (context.Request.ContentLength > 65536)
                throw new RuleException("REQUEST_TOO_LARGE", 413);
            await next(context);
        }
        catch (ClientVersionException error)
        {
            await ApiErrors.Write(context, error.StatusCode, error.Code, version: error.Version);
        }
        catch (RuleException error)
        {
            await ApiErrors.Write(context, error.StatusCode, error.Code, error.BatchId);
        }
        catch (AntiforgeryValidationException)
        {
            await ApiErrors.Write(context, 400, "CSRF_INVALID");
        }
        catch (Exception error)when (error is BadHttpRequestException or JsonException or ArgumentException or InvalidDataException)
        {
            await ApiErrors.Write(context, error is BadHttpRequestException bad ? bad.StatusCode : 400, "VALIDATION_ERROR");
        }
        catch (DbUpdateException error)when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await ApiErrors.Write(context, 409, "RESOURCE_CONFLICT");
        }
        catch (Exception error)
        {
            logger.LogError("Request failed with {ErrorType}; trace {TraceId}", error.GetType().Name, context.TraceIdentifier);
            await ApiErrors.Write(context, 500, "INTERNAL_ERROR");
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ApiMetrics.Duration.Record(elapsed, new KeyValuePair<string, object?>("http.response.status_code", context.Response.StatusCode));
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            // MVC patterns omit the leading slash used by the established metric labels.
            context.RequestServices.GetRequiredService<IValidationRequestMetrics>().Record(route is null ? null : "/" + route.TrimStart('/'), context.Response.StatusCode, elapsed);
        }
    }
}
