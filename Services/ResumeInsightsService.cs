using PKeetDashboard.API.Models;

namespace PKeetDashboard.API.Services;

public static class ResumeInsightsService
{
    public static ResumeInsightsDto Build(ResumeStructuredDocument d)
    {
        var tips = new List<string>();
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(d.Summary))
        {
            missing.Add("Professional summary");
            tips.Add("Add a 2–3 sentence summary that states your role, years of experience, and top strengths.");
        }

        if (string.IsNullOrWhiteSpace(d.Personal.Email))
            missing.Add("Email");

        if (string.IsNullOrWhiteSpace(d.Personal.Phone))
            missing.Add("Phone");

        if (d.Experience.Count == 0)
        {
            missing.Add("Work experience");
            tips.Add("List at least one role with measurable outcomes (%, revenue, latency, users).");
        }
        else
        {
            foreach (var (exp, i) in d.Experience.Select((x, i) => (x, i)))
            {
                var text = (exp.Description + " " + string.Join(" ", exp.Bullets)).ToLowerInvariant();
                if (!ContainsMetric(text))
                    tips.Add($"Experience #{i + 1}: add quantified impact (numbers, %, scale) where possible.");
            }
        }

        if (d.Skills.Sum(g => g.Items.Count) < 5)
            tips.Add("Consider listing more skills grouped by category (frontend, backend, etc.).");

        if (d.Projects.Count > 0)
        {
            foreach (var (p, i) in d.Projects.Select((x, i) => (x, i)))
            {
                if (string.IsNullOrWhiteSpace(p.Technologies))
                    tips.Add($"Project #{i + 1}: add technologies used.");
            }
        }

        return new ResumeInsightsDto
        {
            MissingFields = missing,
            ImprovementTips = tips.Distinct().ToList(),
        };
    }

    private static bool ContainsMetric(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i])) continue;
            // digit with % or nearby scale word
            if (i + 1 < text.Length && text[i + 1] == '%') return true;
            if (text.AsSpan(i).Length >= 2 && char.IsDigit(text[i + 1])) return true;
        }

        return text.Contains('%') || text.Contains("k ") || text.Contains("m ") || text.Contains("million");
    }
}

public class ResumeInsightsDto
{
    public List<string> MissingFields { get; set; } = new();
    public List<string> ImprovementTips { get; set; } = new();
}
