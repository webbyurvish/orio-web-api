using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Data;

public static class SeedData
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.PromoBanners.AnyAsync()) return; // Already seeded

        var contentKeys = new[] { "HeroHeadline", "HeroSubtitle", "HeroCtaPrimary", "HeroCtaSecondary", "HeroCtaSecondaryUrl", "FeatureIntroTitle", "FeatureIntroSubtitle", "FeatureIntroBody", "HowItHelpsTitle", "TestimonialsTitle", "PricingTitle", "PricingSubtitle", "PricingYearlyLabel", "PricingMonthlyLabel", "DominateTitle", "DominateSubtitle", "DiscoverByRoleTitle", "ImproveTitle", "ImproveSubtitle", "TrustTitle", "TrustCtaText", "FooterNewsletterTitle", "CopyrightText", "NavLogin", "NavGetStarted", "GetStartedFree" };
        foreach (var key in contentKeys)
        {
            var value = key switch
            {
                "HeroHeadline" => "Your Real-Time AI Interviewer Assistant",
                "HeroSubtitle" => "Parakeet AI is designed to improve how you prepare for technical interviews.",
                "HeroCtaPrimary" => "Get Started - It's Free",
                "HeroCtaSecondary" => "Schedule a Demo",
                "HeroCtaSecondaryUrl" => "#",
                "FeatureIntroTitle" => "Stop Wasting Time Preparing",
                "FeatureIntroSubtitle" => "Get an AI Interview Assistant that actually helps.",
                "FeatureIntroBody" => "Practice over **500+ questions** across **20+ topics**",
                "HowItHelpsTitle" => "How Parakeet AI Helps",
                "TestimonialsTitle" => "From Our Customers",
                "PricingTitle" => "Pricing",
                "PricingSubtitle" => "Get Over 200+ New Features",
                "PricingYearlyLabel" => "Yearly",
                "PricingMonthlyLabel" => "Monthly",
                "DominateTitle" => "Don't Just Prepare, Dominate",
                "DominateSubtitle" => "Master any technical interview with real-time feedback and 500+ questions.",
                "DiscoverByRoleTitle" => "Discover by Role",
                "ImproveTitle" => "Improve Problem Solving",
                "ImproveSubtitle" => "Parakeet AI is designed to improve how you prepare for technical interviews with real-time feedback, 20+ topics, and 500+ questions.",
                "TrustTitle" => "Still have questions?",
                "TrustCtaText" => "Chat with us",
                "FooterNewsletterTitle" => "Subscribe to our newsletter",
                "CopyrightText" => "© 2024 Parakeet AI. All rights reserved.",
                "NavLogin" => "Login",
                "NavGetStarted" => "Get Started",
                "GetStartedFree" => "Get Started - It's Free",
                _ => key
            };
            db.LandingContents.Add(new LandingContent { Id = Guid.NewGuid(), Key = key, Value = value, SortOrder = 0 });
        }

        db.PromoBanners.Add(new PromoBanner { Id = Guid.NewGuid(), Text = "Special offer for **In India** users: Use code **INDIA25** for 25% off!", CtaText = "UPI Supported", CtaUrl = "#", IsActive = true, SortOrder = 0 });

        foreach (var (key, value) in new[] { ("HappyEngineers", "1000+"), ("QuestionsCount", "500+"), ("TopicsCount", "20+") })
            db.SiteStats.Add(new SiteStat { Id = Guid.NewGuid(), Key = key, Value = value, SortOrder = 0 });

        var companies = new[] { "TechCrunch", "Forbes", "Google", "CNN", "MIT Technology Review", "Bloomberg" };
        for (var i = 0; i < companies.Length; i++)
            db.FeaturedCompanies.Add(new FeaturedCompany { Id = Guid.NewGuid(), Name = companies[i], LogoUrl = null, SortOrder = i });

        var howCards = new[]
        {
            (Title: "Master Specific Topics", Description: "Practice exactly what you want with targeted questions and real-time feedback.", ButtonText: "Get Started Now", IconName: "topic", ExtraData: (string?)null),
            (Title: "Get Real-Time Feedback", Description: "Understand where you're going wrong and improve faster.", ButtonText: "Get Started Now", IconName: "feedback", ExtraData: "{\"score\":\"75%\",\"scoreLabel\":\"Average Score of all your users today: 75%\"}"),
            (Title: "Improve Problem Solving Speed", Description: "Work on problem-solving speed with timed challenges.", ButtonText: "Get Started Now", IconName: "timer", ExtraData: (string?)null),
            (Title: "Support for all technical interviews", Description: "Prepare for any interview from FAANG to startups.", ButtonText: "Get Started Now", IconName: "companies", ExtraData: (string?)null)
        };
        for (var i = 0; i < howCards.Length; i++)
            db.FeatureCards.Add(new FeatureCard { Id = Guid.NewGuid(), SectionKey = "HowItHelps", Title = howCards[i].Title, Description = howCards[i].Description, ButtonText = howCards[i].ButtonText, ButtonUrl = "/signup", IconName = howCards[i].IconName, ExtraData = howCards[i].ExtraData, SortOrder = i });

        var moreCards = new[]
        {
            ("Real Time Feedback", "Get instant feedback on your answers and coding style."),
            ("Question Library", "Access 500+ questions across data structures, algorithms, system design and more."),
            ("Concepts", "Master core concepts with guided practice and explanations."),
            ("Download Reports", "Track your progress with detailed reports you can download.")
        };
        for (var i = 0; i < moreCards.Length; i++)
            db.FeatureCards.Add(new FeatureCard { Id = Guid.NewGuid(), SectionKey = "MoreFeatures", Title = moreCards[i].Item1, Description = moreCards[i].Item2, ButtonText = null, ButtonUrl = null, SortOrder = i });

        var testimonials = new[]
        {
            ("Marilyn Scott", "Sr. Software Engineer", "Google", "Parakeet AI helped me land my dream role. The real-time feedback was a game changer."),
            ("James Chen", "Tech Lead", "Meta", "Best interview prep tool I've used. 500+ questions and real-time feedback."),
            ("Sarah Williams", "Backend Developer", "Amazon", "From zero to offer in 8 weeks. Couldn't have done it without Parakeet AI.")
        };
        for (var i = 0; i < testimonials.Length; i++)
            db.Testimonials.Add(new Testimonial { Id = Guid.NewGuid(), Name = testimonials[i].Item1, Title = testimonials[i].Item2, Company = testimonials[i].Item3, Quote = testimonials[i].Item4, ProfileImageUrl = null, VideoUrl = null, TwitterUrl = null, LinkedInUrl = null, SortOrder = i });

        var topics = new[] { "Data Structures", "Algorithms", "System Design", "Cloud", "UI/UX", "Databases" };
        for (var i = 0; i < topics.Length; i++)
            db.Topics.Add(new Topic { Id = Guid.NewGuid(), Name = topics[i], IconUrl = null, SortOrder = i });

        var freePlanId = Guid.NewGuid();
        var proPlanId = Guid.NewGuid();
        db.PricingPlans.Add(new PricingPlan { Id = freePlanId, Name = "Free", MonthlyPrice = 0, YearlyPrice = 0, YearlyDiscountPercent = null, IsHighlighted = false, ButtonText = "Get Started", ButtonUrl = "/signup", SortOrder = 0 });
        db.PricingPlans.Add(new PricingPlan { Id = proPlanId, Name = "Pro", MonthlyPrice = 15, YearlyPrice = 144, YearlyDiscountPercent = 20, IsHighlighted = true, ButtonText = "Buy Now", ButtonUrl = "#", SortOrder = 1 });

        foreach (var t in new[] { "10 questions / month", "Basic Reports", "Email Support" })
            db.PlanFeatures.Add(new PlanFeature { Id = Guid.NewGuid(), PlanId = freePlanId, Text = t, SortOrder = 0 });
        foreach (var t in new[] { "Unlimited questions", "Premium Reports", "Priority Support", "Interview Simulator" })
            db.PlanFeatures.Add(new PlanFeature { Id = Guid.NewGuid(), PlanId = proPlanId, Text = t, SortOrder = 0 });

        var roles = new[] { ("Software Engineer", "software-engineer"), ("Data Scientist", "data-scientist"), ("Front-End Developer", "frontend"), ("Backend Developer", "backend"), ("DevOps Engineer", "devops"), ("Product Manager", "product-manager") };
        for (var i = 0; i < roles.Length; i++)
            db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = roles[i].Item1, Slug = roles[i].Item2, Url = "#", SortOrder = i });

        var footerCategories = new[] { ("Product", "Features", "#"), ("Product", "Pricing", "#"), ("Product", "Blog", "#"), ("Product", "Success Stories", "#"), ("Product", "Careers", "#"), ("Resources", "Documentation", "#"), ("Resources", "Support", "#"), ("Resources", "FAQ", "#"), ("Resources", "Community", "#"), ("Resources", "Privacy Policy", "#"), ("Resources", "Terms of Service", "#"), ("Company", "About Us", "#"), ("Company", "Contact Us", "#"), ("Company", "Investors", "#") };
        for (var i = 0; i < footerCategories.Length; i++)
            db.FooterLinks.Add(new FooterLink { Id = Guid.NewGuid(), Category = footerCategories[i].Item1, Label = footerCategories[i].Item2, Url = footerCategories[i].Item3, SortOrder = i });

        var socials = new[] { ("LinkedIn", "https://linkedin.com"), ("Twitter", "https://twitter.com"), ("Facebook", "https://facebook.com"), ("Instagram", "https://instagram.com") };
        for (var i = 0; i < socials.Length; i++)
            db.SocialLinks.Add(new SocialLink { Id = Guid.NewGuid(), Platform = socials[i].Item1, Url = socials[i].Item2, SortOrder = i });

        await db.SaveChangesAsync();
    }
}
