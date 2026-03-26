namespace PKeetDashboard.API.DTOs;

public class LandingPageDto
{
    public List<PromoBannerDto> PromoBanners { get; set; } = new();
    public Dictionary<string, string> Content { get; set; } = new();
    public Dictionary<string, string> Stats { get; set; } = new();
    public List<FeaturedCompanyDto> FeaturedCompanies { get; set; } = new();
    public List<FeatureCardDto> FeatureCardsHowItHelps { get; set; } = new();
    public List<FeatureCardDto> FeatureCardsMoreFeatures { get; set; } = new();
    public List<TestimonialDto> Testimonials { get; set; } = new();
    public List<TopicDto> Topics { get; set; } = new();
    public List<PricingPlanDto> PricingPlans { get; set; } = new();
    public List<RoleDto> Roles { get; set; } = new();
    public List<FooterLinkDto> FooterLinks { get; set; } = new();
    public List<SocialLinkDto> SocialLinks { get; set; } = new();
}

public class PromoBannerDto { public string Text { get; set; } = ""; public string? CtaText { get; set; } public string? CtaUrl { get; set; } }
public class FeaturedCompanyDto { public string Name { get; set; } = ""; public string? LogoUrl { get; set; } }
public class FeatureCardDto { public string Title { get; set; } = ""; public string Description { get; set; } = ""; public string? ButtonText { get; set; } public string? ButtonUrl { get; set; } public string? IconName { get; set; } public string? ImageUrl { get; set; } public string? ExtraData { get; set; } }
public class TestimonialDto { public string Name { get; set; } = ""; public string Title { get; set; } = ""; public string Company { get; set; } = ""; public string Quote { get; set; } = ""; public string? ProfileImageUrl { get; set; } public string? VideoUrl { get; set; } public string? TwitterUrl { get; set; } public string? LinkedInUrl { get; set; } }
public class TopicDto { public string Name { get; set; } = ""; public string? IconUrl { get; set; } }
public class PlanFeatureDto { public string Text { get; set; } = ""; }
public class PricingPlanDto { public string Name { get; set; } = ""; public decimal MonthlyPrice { get; set; } public decimal? YearlyPrice { get; set; } public int? YearlyDiscountPercent { get; set; } public bool IsHighlighted { get; set; } public string? ButtonText { get; set; } public string? ButtonUrl { get; set; } public List<PlanFeatureDto> Features { get; set; } = new(); }
public class RoleDto { public string Name { get; set; } = ""; public string Slug { get; set; } = ""; public string? Url { get; set; } }
public class FooterLinkDto { public string Category { get; set; } = ""; public string Label { get; set; } = ""; public string Url { get; set; } = ""; }
public class SocialLinkDto { public string Platform { get; set; } = ""; public string Url { get; set; } = ""; }
