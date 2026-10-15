using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KeyValidation.Infrastructure;
// Action classification is shared by ingestion and queries; new modules extend
// this catalog and the focused resource resolver below.
internal static class AuditActions
{
    internal static string Category(string action) => action is "security.login" or "security.login-two-factor" or "security.login-failed" or "security.logout" ? "login" : "operation";
    internal static string Result(string action, string changes)
    {
        if (action is "backup.create" or "backup.verify" or "webhook.retry")
            return "submitted";
        if (action.EndsWith("-failed", StringComparison.Ordinal))
            return "failed";
        if (action.StartsWith("license.", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(changes);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("success", out var outcome) && outcome.ValueKind == JsonValueKind.False)
                    return "failed";
            }
            catch (JsonException)
            { /* Keep historical unstructured changes intact. */
            }
        }

        return "succeeded";
    }

    internal static string? TargetType(string action) => action.Split('.')[0] switch
    {
        "application" or "risk-policy" or "notification-policy" => "application",
        "plan" or "application-plan" => "plan",
        "batch" => "batch",
        "license" => "license",
        "binding" => "binding",
        "session" => "session",
        "account" or "admin" or "security" => "account",
        "role" => "role",
        "risk" => "risk",
        "webhook" => "webhook",
        "backup" => "backup",
        "customer" => "customer",
        "order" => "order",
        "channel" => "channel",
        "portal-request" => "portal-request",
        "api-credential" => "api-credential",
        "offline-license" => "offline-license",
        _ => null
    };
}
