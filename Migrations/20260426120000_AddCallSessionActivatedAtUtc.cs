using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    public partial class AddCallSessionActivatedAtUtc : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "CallSessions",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "CallSessions");
        }
    }
}

