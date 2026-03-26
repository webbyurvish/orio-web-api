using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LandingController : ControllerBase
{
    private readonly AppDbContext _db;

    public LandingController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType(typeof(LandingPageDto), 200)]
    public async Task<ActionResult<LandingPageDto>> GetLandingPage(CancellationToken ct)
    {
        var promo = await _db.PromoBanners.Where(x => x.IsActive).OrderBy(x => x.SortOrder)
            .Select(x => new PromoBannerDto { Text = x.Text, CtaText = x.CtaText, CtaUrl = x.CtaUrl }).ToListAsync(ct);
        var content = await _db.LandingContents.OrderBy(x => x.SortOrder).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var stats = await _db.SiteStats.OrderBy(x => x.SortOrder).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var companies = await _db.FeaturedCompanies.OrderBy(x => x.SortOrder)
            .Select(x => new FeaturedCompanyDto { Name = x.Name, LogoUrl = x.LogoUrl }).ToListAsync(ct);
        var howCards = await _db.FeatureCards.Where(x => x.SectionKey == "HowItHelps").OrderBy(x => x.SortOrder)
            .Select(x => new FeatureCardDto { Title = x.Title, Description = x.Description, ButtonText = x.ButtonText, ButtonUrl = x.ButtonUrl, IconName = x.IconName, ImageUrl = x.ImageUrl, ExtraData = x.ExtraData }).ToListAsync(ct);
        var moreCards = await _db.FeatureCards.Where(x => x.SectionKey == "MoreFeatures").OrderBy(x => x.SortOrder)
            .Select(x => new FeatureCardDto { Title = x.Title, Description = x.Description, ButtonText = x.ButtonText, ButtonUrl = x.ButtonUrl, IconName = x.IconName, ImageUrl = x.ImageUrl, ExtraData = x.ExtraData }).ToListAsync(ct);
        var testimonials = await _db.Testimonials.OrderBy(x => x.SortOrder)
            .Select(x => new TestimonialDto { Name = x.Name, Title = x.Title, Company = x.Company, Quote = x.Quote, ProfileImageUrl = x.ProfileImageUrl, VideoUrl = x.VideoUrl, TwitterUrl = x.TwitterUrl, LinkedInUrl = x.LinkedInUrl }).ToListAsync(ct);
        var topics = await _db.Topics.OrderBy(x => x.SortOrder)
            .Select(x => new TopicDto { Name = x.Name, IconUrl = x.IconUrl }).ToListAsync(ct);
        var plans = await _db.PricingPlans.Include(p => p.Features).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var pricingPlans = plans.Select(p => new PricingPlanDto
        {
            Name = p.Name,
            MonthlyPrice = p.MonthlyPrice,
            YearlyPrice = p.YearlyPrice,
            YearlyDiscountPercent = p.YearlyDiscountPercent,
            IsHighlighted = p.IsHighlighted,
            ButtonText = p.ButtonText,
            ButtonUrl = p.ButtonUrl,
            Features = p.Features.OrderBy(f => f.SortOrder).Select(f => new PlanFeatureDto { Text = f.Text }).ToList()
        }).ToList();
        var roles = await _db.Roles.OrderBy(x => x.SortOrder)
            .Select(x => new RoleDto { Name = x.Name, Slug = x.Slug, Url = x.Url }).ToListAsync(ct);
        var footerLinks = await _db.FooterLinks.OrderBy(x => x.SortOrder)
            .Select(x => new FooterLinkDto { Category = x.Category, Label = x.Label, Url = x.Url }).ToListAsync(ct);
        var socialLinks = await _db.SocialLinks.OrderBy(x => x.SortOrder)
            .Select(x => new SocialLinkDto { Platform = x.Platform, Url = x.Url }).ToListAsync(ct);

        return Ok(new LandingPageDto
        {
            PromoBanners = promo,
            Content = content,
            Stats = stats,
            FeaturedCompanies = companies,
            FeatureCardsHowItHelps = howCards,
            FeatureCardsMoreFeatures = moreCards,
            Testimonials = testimonials,
            Topics = topics,
            PricingPlans = pricingPlans,
            Roles = roles,
            FooterLinks = footerLinks,
            SocialLinks = socialLinks
        });
    }

    [HttpPost("newsletter")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SubscribeNewsletter([FromBody] NewsletterRequest request, CancellationToken ct)
    {
        var email = request?.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return BadRequest(new { message = "Valid email is required." });

        if (await _db.NewsletterSubscriptions.AnyAsync(x => x.Email == email, ct))
            return Ok(new { message = "Already subscribed." });

        _db.NewsletterSubscriptions.Add(new NewsletterSubscription { Id = Guid.NewGuid(), Email = email });
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Subscribed successfully." });
    }
}

public class NewsletterRequest
{
    public string? Email { get; set; }
}
