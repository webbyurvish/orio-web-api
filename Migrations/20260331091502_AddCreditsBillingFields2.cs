using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditsBillingFields2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditsCharged",
                table: "CallSessions",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CallCredits",
                table: "Users",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "StripePaymentReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeSessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreditsApplied = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripePaymentReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentReceipts_StripeSessionId",
                table: "StripePaymentReceipts",
                column: "StripeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentReceipts_UserId",
                table: "StripePaymentReceipts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StripePaymentReceipts");
            migrationBuilder.DropColumn(name: "CreditsCharged", table: "CallSessions");
            migrationBuilder.DropColumn(name: "CallCredits", table: "Users");
        }
    }
}
