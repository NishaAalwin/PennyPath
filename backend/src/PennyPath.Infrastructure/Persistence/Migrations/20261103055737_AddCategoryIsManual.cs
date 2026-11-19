using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PennyPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIsManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CategoryIsManual",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryIsManual",
                table: "Transactions");
        }
    }
}
