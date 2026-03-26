using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    public partial class LandingPageTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromoBanners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CtaText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CtaUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PromoBanners", x => x.Id));

            migrationBuilder.CreateTable(
                name: "SiteStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_SiteStats", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_SiteStats_Key", table: "SiteStats", column: "Key", unique: true);

            migrationBuilder.CreateTable(
                name: "FeaturedCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FeaturedCompanies", x => x.Id));

            migrationBuilder.CreateTable(
                name: "FeatureCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ButtonText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ButtonUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IconName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtraData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FeatureCards", x => x.Id));

            migrationBuilder.CreateTable(
                name: "Testimonials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Company = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProfileImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VideoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TwitterUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedInUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Testimonials", x => x.Id));

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IconUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Topics", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PricingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    YearlyPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    YearlyDiscountPercent = table.Column<int>(type: "int", nullable: true),
                    IsHighlighted = table.Column<bool>(type: "bit", nullable: false),
                    ButtonText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ButtonUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PricingPlans", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PlanFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanFeatures", x => x.Id);
                    table.ForeignKey(name: "FK_PlanFeatures_PricingPlans_PlanId", column: x => x.PlanId, principalTable: "PricingPlans", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_PlanFeatures_PlanId", table: "PlanFeatures", column: "PlanId");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Roles", x => x.Id));

            migrationBuilder.CreateTable(
                name: "FooterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FooterLinks", x => x.Id));

            migrationBuilder.CreateTable(
                name: "SocialLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_SocialLinks", x => x.Id));

            migrationBuilder.CreateTable(
                name: "NewsletterSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_NewsletterSubscriptions", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_NewsletterSubscriptions_Email", table: "NewsletterSubscriptions", column: "Email");

            migrationBuilder.CreateTable(
                name: "LandingContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LandingContents", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_LandingContents_Key", table: "LandingContents", column: "Key", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PromoBanners");
            migrationBuilder.DropTable(name: "SiteStats");
            migrationBuilder.DropTable(name: "FeaturedCompanies");
            migrationBuilder.DropTable(name: "FeatureCards");
            migrationBuilder.DropTable(name: "Testimonials");
            migrationBuilder.DropTable(name: "Topics");
            migrationBuilder.DropTable(name: "PlanFeatures");
            migrationBuilder.DropTable(name: "PricingPlans");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "FooterLinks");
            migrationBuilder.DropTable(name: "SocialLinks");
            migrationBuilder.DropTable(name: "NewsletterSubscriptions");
            migrationBuilder.DropTable(name: "LandingContents");
        }
    }
}
