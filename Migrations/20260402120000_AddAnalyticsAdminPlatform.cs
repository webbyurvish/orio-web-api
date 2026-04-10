using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PKeetDashboard.API.Data;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260402120000_AddAnalyticsAdminPlatform")]
    public partial class AddAnalyticsAdminPlatform : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActiveAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "UserFeedbacks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentimentTags",
                table: "UserFeedbacks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AmountUsdCents",
                table: "StripePaymentReceipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "StripePaymentReceipts",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CallSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeploymentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptTokens = table.Column<int>(type: "int", nullable: true),
                    CompletionTokens = table.Column<int>(type: "int", nullable: true),
                    TotalTokens = table.Column<int>(type: "int", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsageLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_CreatedAtUtc",
                table: "AnalyticsEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType_CreatedAtUtc",
                table: "AnalyticsEvents",
                columns: new[] { "EventType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserId_CreatedAtUtc",
                table: "AnalyticsEvents",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageLogs_CreatedAtUtc",
                table: "AiUsageLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageLogs_UserId_CreatedAtUtc",
                table: "AiUsageLogs",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiUsageLogs");
            migrationBuilder.DropTable(name: "AnalyticsEvents");
            migrationBuilder.DropColumn(name: "Currency", table: "StripePaymentReceipts");
            migrationBuilder.DropColumn(name: "AmountUsdCents", table: "StripePaymentReceipts");
            migrationBuilder.DropColumn(name: "SentimentTags", table: "UserFeedbacks");
            migrationBuilder.DropColumn(name: "Rating", table: "UserFeedbacks");
            migrationBuilder.DropColumn(name: "LastActiveAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "IsAdmin", table: "Users");
        }
    }
}
