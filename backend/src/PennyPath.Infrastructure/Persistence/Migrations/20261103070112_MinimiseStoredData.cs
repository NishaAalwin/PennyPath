using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PennyPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MinimiseStoredData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnparsedMessages");

            migrationBuilder.DropColumn(
                name: "Account",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnparsedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnparsedMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnparsedMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnparsedMessages_UserId_RawHash",
                table: "UnparsedMessages",
                columns: new[] { "UserId", "RawHash" },
                unique: true);
        }
    }
}
