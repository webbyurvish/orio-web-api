using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<PromoBanner> PromoBanners => Set<PromoBanner>();
    public DbSet<SiteStat> SiteStats => Set<SiteStat>();
    public DbSet<FeaturedCompany> FeaturedCompanies => Set<FeaturedCompany>();
    public DbSet<FeatureCard> FeatureCards => Set<FeatureCard>();
    public DbSet<Testimonial> Testimonials => Set<Testimonial>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<FooterLink> FooterLinks => Set<FooterLink>();
    public DbSet<SocialLink> SocialLinks => Set<SocialLink>();
    public DbSet<NewsletterSubscription> NewsletterSubscriptions => Set<NewsletterSubscription>();
    public DbSet<LandingContent> LandingContents => Set<LandingContent>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<CallSessionMessage> CallSessionMessages => Set<CallSessionMessage>();
    public DbSet<StripePaymentReceipt> StripePaymentReceipts => Set<StripePaymentReceipt>();
    public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
    public DbSet<UserDiscoveryResponse> UserDiscoveryResponses => Set<UserDiscoveryResponse>();
    public DbSet<UserFeedback> UserFeedbacks => Set<UserFeedback>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.GoogleId).IsUnique().HasFilter("[GoogleId] IS NOT NULL");
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.GoogleId).HasMaxLength(128);
            entity.Property(e => e.CallCredits).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<PromoBanner>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(500);
        });

        modelBuilder.Entity<SiteStat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<FeaturedCompany>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<FeatureCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SectionKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Testimonial>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Topic>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<PricingPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.MonthlyPrice).HasColumnType("decimal(10,2)");
            entity.Property(e => e.YearlyPrice).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<PlanFeature>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Plan).WithMany(p => p.Features).HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<FooterLink>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<SocialLink>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<NewsletterSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<LandingContent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Resume>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.StructuredDataJson).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<CallSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(4000);
            entity.Property(e => e.Language).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ExtraContext).HasMaxLength(2000);
            entity.Property(e => e.AiModel).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreditsCharged).HasColumnType("decimal(10,2)");
            entity.Property(e => e.AiNotes).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<StripePaymentReceipt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.StripeSessionId).IsUnique();
            entity.Property(e => e.StripeSessionId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ProductId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreditsApplied).HasColumnType("decimal(10,2)");
            entity.Property(e => e.Currency).HasMaxLength(10);
        });

        modelBuilder.Entity<CallSessionMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CallSessionId);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Content).IsRequired();
            entity.HasOne<CallSession>().WithMany().HasForeignKey(e => e.CallSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailVerificationCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CodeHash).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ExpiresAtUtc).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.LastSentAtUtc).IsRequired();
        });

        modelBuilder.Entity<UserDiscoveryResponse>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.Source).IsRequired().HasMaxLength(80);
            entity.Property(e => e.OtherText).HasMaxLength(200);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<UserFeedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.SentimentTags).HasMaxLength(500);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => new { e.EventType, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc });
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc });
            entity.Property(e => e.DeploymentName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EstimatedCostUsd).HasColumnType("decimal(12,6)");
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
