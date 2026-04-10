using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PKeetDashboard.API.Models;

namespace PKeetDashboard.API.Services;

public class ResumeStructuredParsingService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResumeStructuredParsingService> _logger;

    public ResumeStructuredParsingService(IConfiguration configuration, ILogger<ResumeStructuredParsingService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ResumeStructuredDocument> ParseResumeTextAsync(string plainText, CancellationToken ct = default)
    {
        var doc = EmptyDocument();
        if (string.IsNullOrWhiteSpace(plainText))
        {
            doc.ParseMeta = new ParseMetaDto
            {
                OverallConfidence = 0,
                Warnings = new List<string> { "No text could be extracted from the file." },
            };
            return doc;
        }

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Azure OpenAI is not configured.");

        var client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        var options = new ChatCompletionsOptions { DeploymentName = deploymentName, Temperature = 0.1f, MaxTokens = 4096 };
        options.Messages.Add(new ChatRequestSystemMessage(ResumeJsonSystemPrompt));
        options.Messages.Add(new ChatRequestUserMessage(
            "Extract structured resume data from the following plain text. Return ONLY valid JSON, no markdown.\n\n---\n" + plainText));

        Response<ChatCompletions> responseWrap;
        try
        {
            responseWrap = await client.GetChatCompletionsAsync(options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume AI parse failed");
            throw;
        }

        var content = responseWrap.Value.Choices.FirstOrDefault()?.Message?.Content ?? "";
        content = StripJsonFence(content);

        ResumeStructuredDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ResumeStructuredDocument>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize resume JSON; returning empty shell. Raw length={Len}", content.Length);
            doc.ParseMeta = new ParseMetaDto
            {
                OverallConfidence = 0.2,
                Warnings = new List<string> { "AI returned invalid JSON. You can fill the form manually." },
            };
            return doc;
        }

        if (parsed == null)
            return doc;

        NormalizeDocument(parsed);
        parsed.ParseMeta ??= new ParseMetaDto();
        parsed.ParseMeta.Warnings ??= new List<string>();
        if (parsed.ParseMeta.FieldConfidence.Count == 0)
            ApplyDefaultConfidence(parsed);
        parsed.ParseMeta.AiFilledFieldPaths = CollectFilledPaths(parsed);
        parsed.ParseMeta.OverallConfidence = ComputeOverallConfidence(parsed);

        if (string.IsNullOrWhiteSpace(parsed.Summary))
            parsed.ParseMeta.Warnings.Add("No professional summary detected — consider adding one.");

        return parsed;
    }

    private static void NormalizeDocument(ResumeStructuredDocument d)
    {
        d.Personal ??= new PersonalBlock();
        d.Skills ??= new List<SkillGroupDto>();
        d.Experience ??= new List<ExperienceEntryDto>();
        d.Education ??= new List<EducationEntryDto>();
        d.Projects ??= new List<ProjectEntryDto>();
        d.Certifications ??= new List<CertificationEntryDto>();
        d.OtherSections ??= new List<OtherSectionDto>();
        d.SectionOrder ??= new List<string>();

        foreach (var g in d.Skills)
        {
            g.Category = NormalizeCategory(g.Category);
            g.Items = (g.Items ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        }

        d.Skills = d.Skills.Where(g => g.Items.Count > 0).ToList();

        if (d.SectionOrder.Count == 0)
        {
            d.SectionOrder = new List<string>
            {
                "title", "personal", "summary", "skills", "experience", "education", "projects", "certifications", "other",
            };
        }
    }

    private static string NormalizeCategory(string? c)
    {
        var raw = (c ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "other";
        var x = raw.ToLowerInvariant().Replace(" ", "").Replace("-", "");
        return x switch
        {
            "frontend" => "frontend",
            "backend" => "backend",
            "database" => "database",
            "devops" => "devops",
            "tools" or "tooling" => "tools",
            "softskills" or "soft" => "softSkills",
            "aiautomation" or "ai" => "aiAutomation",
            _ => raw.Length > 60 ? raw[..60] : raw,
        };
    }

    private static void ApplyDefaultConfidence(ResumeStructuredDocument d)
    {
        var m = d.ParseMeta!;
        void Set(string path, bool filled) => m.FieldConfidence[path] = filled ? 0.85 : 0.2;
        Set("personal.fullName", !string.IsNullOrWhiteSpace(d.Personal.FullName));
        Set("personal.email", !string.IsNullOrWhiteSpace(d.Personal.Email));
        Set("personal.phone", !string.IsNullOrWhiteSpace(d.Personal.Phone));
        Set("personal.location", !string.IsNullOrWhiteSpace(d.Personal.Location));
        Set("summary", !string.IsNullOrWhiteSpace(d.Summary));
        Set("skills", d.Skills.Sum(g => g.Items.Count) > 0);
        Set("experience", d.Experience.Count > 0);
        Set("education", d.Education.Count > 0);
        Set("projects", d.Projects.Count > 0);
    }

    private static double ComputeOverallConfidence(ResumeStructuredDocument d)
    {
        if (d.ParseMeta?.FieldConfidence == null || d.ParseMeta.FieldConfidence.Count == 0)
            return 0.5;
        return Math.Round(d.ParseMeta.FieldConfidence.Values.DefaultIfEmpty(0.5).Average(), 2);
    }

    private static List<string> CollectFilledPaths(ResumeStructuredDocument d)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Personal.FullName)) paths.Add("personal.fullName");
        if (!string.IsNullOrWhiteSpace(d.Personal.Email)) paths.Add("personal.email");
        if (!string.IsNullOrWhiteSpace(d.Personal.Phone)) paths.Add("personal.phone");
        if (!string.IsNullOrWhiteSpace(d.Personal.Location)) paths.Add("personal.location");
        if (!string.IsNullOrWhiteSpace(d.Summary)) paths.Add("summary");
        if (d.Skills.Any(g => g.Items.Count > 0)) paths.Add("skills");
        for (var i = 0; i < d.Experience.Count; i++) paths.Add($"experience.{i}");
        for (var i = 0; i < d.Education.Count; i++) paths.Add($"education.{i}");
        for (var i = 0; i < d.Projects.Count; i++) paths.Add($"projects.{i}");
        for (var i = 0; i < d.Certifications.Count; i++) paths.Add($"certifications.{i}");
        for (var i = 0; i < d.OtherSections.Count; i++) paths.Add($"otherSections.{i}");
        return paths;
    }

    public static ResumeStructuredDocument EmptyDocument() => new()
    {
        Personal = new PersonalBlock(),
        SectionOrder = new List<string>
        {
            "title", "personal", "summary", "skills", "experience", "education", "projects", "certifications", "other",
        },
        ParseMeta = new ParseMetaDto { OverallConfidence = 0, Warnings = new List<string>() },
    };

    private static string StripJsonFence(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            s = s[7..];
        else if (s.StartsWith("```"))
            s = s[3..];
        if (s.EndsWith("```"))
            s = s[..^3];
        return s.Trim();
    }

    private const string ResumeJsonSystemPrompt = """
You are an expert resume parser. Output a single JSON object only (no markdown fences).

Schema (all keys required; use empty string or empty array if unknown):
{
  "personal": { "fullName": "", "email": "", "phone": "", "location": "" },
  "summary": "",
  "skills": [ { "category": "", "items": ["skill"] } ],
  "experience": [ { "company": "", "role": "", "duration": "", "location": "", "description": "", "bullets": ["..."] } ],
  "education": [ { "school": "", "degree": "", "timePeriod": "", "location": "", "description": "" } ],
  "projects": [ { "title": "", "description": "", "technologies": "" } ],
  "certifications": [ { "title": "", "issuer": "", "date": "", "description": "" } ],
  "otherSections": [ { "title": "", "description": "" } ],
  "sectionOrder": ["title","personal","summary","skills","experience","education","projects","certifications","other"],
  "parseMeta": {
    "overallConfidence": 0.0,
    "fieldConfidence": { "personal.email": 0.0 },
    "warnings": [],
    "aiFilledFieldPaths": []
  }
}

Rules:
- Map varied headings: "Work History", "Professional Experience", "Employment" → experience.
- Skills categorization:
  - If the resume text includes an explicit skills heading/type (e.g. "Frontend:", "Backend:", "DevOps & Cloud:", "Desktop Development:"), preserve that wording in `category`.
  - Otherwise infer a short category label and group items (React→Frontend, SQL→Database, Docker→DevOps, Git→Tools, Leadership→Soft skills, Copilot/Azure AI→AI / Automation).
  - Prefer consistent labels, but do not force everything into "other" if you can infer a reasonable category.
- Bullets: prefer array entries; merge narrative into description if bullets not explicit.
- Extract email/phone with regex-like accuracy; never invent employers or degrees.
- If text is ambiguous, lower fieldConfidence for that area and add a short warning in parseMeta.warnings.
- aiFilledFieldPaths: list dot-paths for fields you populated with non-empty values (same pattern as fieldConfidence keys).
""";
}
