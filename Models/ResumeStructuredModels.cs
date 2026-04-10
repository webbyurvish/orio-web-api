namespace PKeetDashboard.API.Models;

public class ResumeStructuredDocument
{
    public PersonalBlock Personal { get; set; } = new();
    public string Summary { get; set; } = "";
    public List<SkillGroupDto> Skills { get; set; } = new();
    public List<ExperienceEntryDto> Experience { get; set; } = new();
    public List<EducationEntryDto> Education { get; set; } = new();
    public List<ProjectEntryDto> Projects { get; set; } = new();
    public List<CertificationEntryDto> Certifications { get; set; } = new();
    public List<OtherSectionDto> OtherSections { get; set; } = new();
    public List<string> SectionOrder { get; set; } = new();
    public ParseMetaDto? ParseMeta { get; set; }
}

public class PersonalBlock
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";
}

public class SkillGroupDto
{
    /// <summary>frontend | backend | database | devops | tools | softSkills | aiAutomation | other</summary>
    public string Category { get; set; } = "other";
    public List<string> Items { get; set; } = new();
}

public class ExperienceEntryDto
{
    public string Company { get; set; } = "";
    public string Role { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Bullets { get; set; } = new();
}

public class EducationEntryDto
{
    public string School { get; set; } = "";
    public string Degree { get; set; } = "";
    public string TimePeriod { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ProjectEntryDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Technologies { get; set; } = "";
}

public class CertificationEntryDto
{
    public string Title { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
}

public class OtherSectionDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ParseMetaDto
{
    public double OverallConfidence { get; set; }
    public Dictionary<string, double> FieldConfidence { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AiFilledFieldPaths { get; set; } = new();
}
