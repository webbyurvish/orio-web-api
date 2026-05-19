using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PKeetDashboard.API.Data;

#nullable disable

namespace PKeetDashboard.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260516120000_RenameStripeReceiptsToPaymentReceipts")]
public partial class RenameStripeReceiptsToPaymentReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "StripePaymentReceipts",
            newName: "PaymentReceipts");

        migrationBuilder.RenameColumn(
            name: "StripeSessionId",
            table: "PaymentReceipts",
            newName: "RazorpayOrderId");

        migrationBuilder.RenameColumn(
            name: "AmountUsdCents",
            table: "PaymentReceipts",
            newName: "AmountInrPaise");

        migrationBuilder.RenameIndex(
            name: "IX_StripePaymentReceipts_StripeSessionId",
            table: "PaymentReceipts",
            newName: "IX_PaymentReceipts_RazorpayOrderId");

        migrationBuilder.RenameIndex(
            name: "IX_StripePaymentReceipts_UserId",
            table: "PaymentReceipts",
            newName: "IX_PaymentReceipts_UserId");

        migrationBuilder.AddColumn<string>(
            name: "RazorpayPaymentId",
            table: "PaymentReceipts",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RazorpayPaymentId",
            table: "PaymentReceipts");

        migrationBuilder.RenameIndex(
            name: "IX_PaymentReceipts_UserId",
            table: "PaymentReceipts",
            newName: "IX_StripePaymentReceipts_UserId");

        migrationBuilder.RenameIndex(
            name: "IX_PaymentReceipts_RazorpayOrderId",
            table: "PaymentReceipts",
            newName: "IX_StripePaymentReceipts_StripeSessionId");

        migrationBuilder.RenameColumn(
            name: "AmountInrPaise",
            table: "PaymentReceipts",
            newName: "AmountUsdCents");

        migrationBuilder.RenameColumn(
            name: "RazorpayOrderId",
            table: "PaymentReceipts",
            newName: "StripeSessionId");

        migrationBuilder.RenameTable(
            name: "PaymentReceipts",
            newName: "StripePaymentReceipts");
    }
}
