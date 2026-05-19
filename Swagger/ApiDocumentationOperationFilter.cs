using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PKeetDashboard.API.Swagger;

/// <summary>Applies human-readable summaries and descriptions to Swagger operations.</summary>
public sealed class ApiDocumentationOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, (string Summary, string Description)> Catalog =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Auth
            ["POST api/auth/register"] = (
                "Register with email and password",
                "Creates a user account and returns a JWT plus profile. Prefer the email verification flow when enabled."),
            ["POST api/auth/register/initiate"] = (
                "Start email verification signup",
                "Sends a verification code to the supplied email before account creation."),
            ["POST api/auth/register/verify"] = (
                "Complete email verification signup",
                "Verifies the emailed code and creates the account with the supplied profile details."),
            ["POST api/auth/login"] = (
                "Sign in with email and password",
                "Authenticates the user and returns a JWT plus profile."),
            ["POST api/auth/google-login"] = (
                "Sign in with Google",
                "Exchanges a Google ID token for a Smeed AI JWT and profile."),
            ["GET api/auth/me"] = (
                "Get current user profile",
                "Returns the authenticated user's profile, credits, and entitlements."),
            ["POST api/auth/discovery"] = (
                "Save onboarding discovery response",
                "Stores how the user heard about the product after first login."),
            ["POST api/auth/desktop/initiate"] = (
                "Start desktop OAuth handoff",
                "Issues a short-lived authorization code and redirect URL for the desktop app callback."),
            ["POST api/auth/exchange"] = (
                "Exchange desktop authorization code",
                "Redeems a desktop handoff code for access and refresh tokens. Anonymous; rate limited."),

            // Call sessions
            ["POST api/callsessions"] = (
                "Create interview session",
                "Creates a draft session with job context, language, resume, and AI preferences. Free sessions enforce one pending/active session and a cooldown after use."),
            ["PUT api/callsessions/{id}"] = (
                "Update interview session",
                "Updates editable session fields before or between activations."),
            ["DELETE api/callsessions/{id}"] = (
                "Delete interview session",
                "Permanently removes a session owned by the current user."),
            ["GET api/callsessions"] = (
                "List interview sessions",
                "Returns a paginated list of the user's sessions, optionally filtered by view."),
            ["GET api/callsessions/{id}"] = (
                "Get interview session",
                "Returns one session by id for the authenticated user."),
            ["GET api/callsessions/{id}/messages"] = (
                "List session transcript messages",
                "Returns saved transcript messages for a session in chronological order."),
            ["POST api/callsessions/{id}/messages"] = (
                "Append transcript message",
                "Adds a single transcript line to a session."),
            ["POST api/callsessions/{id}/messages/bulk"] = (
                "Append transcript messages in bulk",
                "Adds multiple transcript lines in one request."),
            ["POST api/callsessions/{id}/activate"] = (
                "Activate interview session",
                "Marks a session active so the desktop or browser client can start the interview."),
            ["POST api/callsessions/{id}/extend"] = (
                "Request session extension",
                "Legacy endpoint; extensions are no longer used because billing is usage-based at end."),
            ["POST api/callsessions/{id}/end"] = (
                "End interview session",
                "Ends the session, charges paid minutes if applicable, and may generate AI notes when transcript saving is enabled."),
            ["GET api/callsessions/{id}/ai-notes"] = (
                "Get AI call notes",
                "Returns generated markdown notes for a completed session."),
            ["POST api/callsessions/{id}/ai-notes/generate"] = (
                "Generate AI call notes",
                "Builds or refreshes markdown notes from the saved transcript."),
            ["POST api/callsessions/{id}/ai-usage"] = (
                "Increment AI usage counter",
                "Increments per-session AI answer usage for analytics and limits."),

            // Desktop AI
            ["POST api/desktop/ai/answer"] = (
                "Generate interview answer",
                "Returns a single non-streaming Azure OpenAI answer using optional resume context and client system prompt."),
            ["POST api/desktop/ai/clarify-transcript-question"] = (
                "Clarify transcript question",
                "Cleans noisy speech-to-text into a concise HEADING/BODY question for downstream answering."),
            ["POST api/desktop/ai/answer-stream"] = (
                "Stream interview answer",
                "Streams Azure OpenAI output as NDJSON lines with `{\"d\":\"token\"}` deltas."),
            ["POST api/desktop/ai/screenshot-answer-stream"] = (
                "Stream screenshot answer",
                "OCRs a screenshot with Azure Computer Vision, then streams an interview-style answer over NDJSON."),

            // Desktop speech
            ["GET api/desktop/speech/token"] = (
                "Issue Azure Speech token",
                "Returns a short-lived Azure Speech authorization token and region for desktop STT."),

            // Razorpay
            ["GET api/payments/razorpay/checkout-options"] = (
                "Get Razorpay checkout options",
                "Returns public key id, INR currency, and test-mode flag."),
            ["POST api/payments/razorpay/create-order"] = (
                "Create Razorpay order",
                "Creates an INR order for credits or unlimited plans; client opens Razorpay Checkout."),
            ["POST api/payments/razorpay/verify-payment"] = (
                "Verify Razorpay payment",
                "Verifies signature and applies credits or unlimited entitlements."),

            // Admin
            ["GET api/admin/dashboard"] = (
                "Admin dashboard summary",
                "Aggregated KPIs for the selected UTC date range."),
            ["GET api/admin/users"] = (
                "Admin user list",
                "Paginated user rows with optional search and date filters."),
            ["GET api/admin/subscriptions"] = (
                "Admin subscription summary",
                "Subscription metrics for the selected UTC date range."),
            ["GET api/admin/usage"] = (
                "Admin usage summary",
                "Session and product usage metrics for the selected UTC date range."),
            ["GET api/admin/ai-metrics"] = (
                "Admin AI metrics",
                "Azure OpenAI usage, latency, and success metrics."),
            ["GET api/admin/funnel"] = (
                "Admin funnel metrics",
                "Signup and activation funnel counts for the selected UTC date range."),
            ["GET api/admin/feedback"] = (
                "Admin feedback list",
                "Paginated user feedback for the selected UTC date range."),
            ["GET api/admin/system-metrics"] = (
                "Admin system metrics",
                "Operational counters and health-style metrics."),
            ["GET api/admin/export/events.csv"] = (
                "Export analytics events CSV",
                "Downloads up to 50,000 analytics events as CSV for the selected UTC date range."),

            // Feedback
            ["POST api/feedback"] = (
                "Submit product feedback",
                "Stores authenticated user feedback for review."),

            // Resumes
            ["POST api/resumes/parse-upload"] = (
                "Upload and parse resume",
                "Accepts PDF or DOCX, extracts text, parses structured JSON with Azure OpenAI, and persists the resume."),
            ["POST api/resumes/upload"] = (
                "Upload resume file",
                "Stores an uploaded PDF or DOCX without running the full parse pipeline."),
            ["POST api/resumes/manual"] = (
                "Create manual resume",
                "Creates an empty structured resume for manual editing in the dashboard."),
            ["GET api/resumes"] = (
                "List resumes",
                "Returns the authenticated user's resumes."),
            ["POST api/resumes/{id}/parse"] = (
                "Re-parse resume file",
                "Re-runs AI parsing on an existing uploaded resume file."),
            ["GET api/resumes/{id}/detail"] = (
                "Get resume detail",
                "Returns structured resume JSON and metadata."),
            ["PATCH api/resumes/{id}"] = (
                "Patch resume metadata",
                "Updates lightweight resume fields such as title."),
            ["PUT api/resumes/{id}/structured"] = (
                "Replace structured resume",
                "Saves the full structured resume document from the editor."),
            ["GET api/resumes/{id}/insights"] = (
                "Get resume insights",
                "Returns AI-generated insights for the resume."),
            ["POST api/resumes/{id}/ai/improve"] = (
                "Improve resume text with AI",
                "Rewrites selected resume content without inventing facts."),
            ["DELETE api/resumes/{id}"] = (
                "Delete resume",
                "Deletes a resume owned by the current user."),
            ["GET api/resumes/{id}/file"] = (
                "Download resume file",
                "Downloads the original uploaded resume binary."),
            ["GET api/resumes/{id}/text"] = (
                "Get resume plain text",
                "Returns extracted plain text for the resume."),

            // Analytics
            ["POST api/analytics/events"] = (
                "Ingest analytics events",
                "Accepts up to 100 client telemetry events in one authenticated batch."),

            // Landing
            ["GET api/landing"] = (
                "Get landing page content",
                "Public marketing content: promos, features, pricing, testimonials, and footer links."),
            ["POST api/landing/newsletter"] = (
                "Subscribe to newsletter",
                "Public newsletter signup for the marketing site."),
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var key = BuildKey(context.ApiDescription);
        if (!Catalog.TryGetValue(key, out var doc))
            return;

        if (string.IsNullOrWhiteSpace(operation.Summary))
            operation.Summary = doc.Summary;

        if (string.IsNullOrWhiteSpace(operation.Description))
            operation.Description = doc.Description;
    }

    private static string BuildKey(ApiDescription apiDescription)
    {
        var method = apiDescription.HttpMethod?.Trim().ToUpperInvariant() ?? "GET";
        var path = (apiDescription.RelativePath ?? string.Empty).Trim().TrimEnd('/');
        path = Regex.Replace(path, "\\{([^}:]+):[^}]+\\}", "{$1}");
        return $"{method} {path}";
    }
}
