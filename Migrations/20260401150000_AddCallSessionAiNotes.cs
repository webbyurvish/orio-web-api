using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PKeetDashboard.API.Data;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260401150000_AddCallSessionAiNotes")]
    public partial class AddCallSessionAiNotes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiNotes",
                table: "CallSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiNotesUpdatedAt",
                table: "CallSessions",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AiNotes", table: "CallSessions");
            migrationBuilder.DropColumn(name: "AiNotesUpdatedAt", table: "CallSessions");
        }
    }
}

