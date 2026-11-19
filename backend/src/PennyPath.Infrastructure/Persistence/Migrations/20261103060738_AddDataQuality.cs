using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PennyPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRefund",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundOfId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnparsedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RawHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_Transactions_UserId_Amount_Date",
                table: "Transactions",
                columns: new[] { "UserId", "Amount", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_UnparsedMessages_UserId_RawHash",
                table: "UnparsedMessages",
                columns: new[] { "UserId", "RawHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnparsedMessages");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Amount_Date",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DuplicateOfId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsRefund",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RefundOfId",
                table: "Transactions");
        }
    }
}
