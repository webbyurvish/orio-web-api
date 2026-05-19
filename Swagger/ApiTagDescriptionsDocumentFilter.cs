using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PKeetDashboard.API.Swagger;

/// <summary>Supplies Swagger UI tag descriptions for each API area.</summary>
public sealed class ApiTagDescriptionsDocumentFilter : IDocumentFilter
{
    private static readonly IReadOnlyDictionary<string, string> TagDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auth"] = "Account registration, email/password and Google sign-in, profile, onboarding discovery, and desktop OAuth handoff.",
            ["CallSessions"] = "Interview session lifecycle: create and configure sessions, activate/end, transcript messages, AI usage counters, and generated call notes.",
            ["DesktopAi"] = "Azure OpenAI proxy for the desktop and browser interview clients: answers, transcript cleanup, and screenshot OCR answers.",
            ["DesktopSpeech"] = "Short-lived Azure Speech authorization tokens for desktop speech-to-text.",
            ["RazorpayPayments"] = "Razorpay Checkout (INR) for interview credits, subscriptions, and lifetime access.",
            ["Admin"] = "Admin-only analytics, user lists, exports, and operational metrics.",
            ["Feedback"] = "Authenticated user feedback submissions.",
            ["Resumes"] = "Resume upload, parsing, structured editing, insights, and AI-assisted rewrites.",
            ["AnalyticsEvents"] = "Client-side analytics event ingestion for product telemetry.",
            ["Landing"] = "Public marketing landing page content and newsletter signup.",
        };

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Tags == null)
            swaggerDoc.Tags = new List<OpenApiTag>();

        var byName = swaggerDoc.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToDictionary(t => t.Name!, t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, description) in TagDescriptions)
        {
            if (byName.TryGetValue(name, out var existing))
            {
                existing.Description = description;
                continue;
            }

            swaggerDoc.Tags.Add(new OpenApiTag { Name = name, Description = description });
        }
    }
}
