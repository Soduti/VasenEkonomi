using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vasen.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnContribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OwnContributionOre",
                table: "Withdrawals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnContributionOre",
                table: "Withdrawals");
        }
    }
}
