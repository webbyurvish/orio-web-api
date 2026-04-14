using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PKeetDashboard.API.Data;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260413120000_AddCallSessionNaturalSpeakingMode")]
    public partial class AddCallSessionNaturalSpeakingMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NaturalSpeakingMode",
                table: "CallSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NaturalSpeakingMode", table: "CallSessions");
        }
    }
}
