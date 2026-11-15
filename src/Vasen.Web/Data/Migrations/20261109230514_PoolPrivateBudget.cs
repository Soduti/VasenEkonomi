using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vasen.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PoolPrivateBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing private budget lines are disposable test data. Receipts are unaffected.
            migrationBuilder.Sql("DELETE FROM \"BudgetLines\" WHERE \"Source\" = 1;");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_PrivateYear",
                table: "BudgetLines",
                column: "Year",
                unique: true,
                filter: "\"Source\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BudgetLines_PrivateYear",
                table: "BudgetLines");
        }
    }
}
