using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PKeetDashboard.API.Data;

#nullable disable

namespace PKeetDashboard.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260402183000_ResumeStructuredData")]
public partial class ResumeStructuredData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StructuredDataJson",
            table: "Resumes",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Resumes",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StructuredDataJson", table: "Resumes");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Resumes");
    }
}
