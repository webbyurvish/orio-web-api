using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKeetDashboard.API.Migrations
{
    public partial class CallSessionMessagesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallSessionMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallSessionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallSessionMessages_CallSessions_CallSessionId",
                        column: x => x.CallSessionId,
                        principalTable: "CallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessionMessages_CallSessionId",
                table: "CallSessionMessages",
                column: "CallSessionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CallSessionMessages");
        }
    }
}
