using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vasen.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "Receipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccessLevel",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing members keep receipt registration; new accounts default to read-only.
            migrationBuilder.Sql("UPDATE AspNetUsers SET AccessLevel = 1 WHERE IsOwner = 0 AND IsAnonymized = 0;");
            // Responsibility is not authorship: never guess the creator from MemberId.
            migrationBuilder.Sql("""
                UPDATE Receipts SET CreatedById = (
                    SELECT ActorId FROM AuditEntries
                    WHERE Entity = 'Kvitto' AND EntityId = Receipts.Id AND Action = 'Skapat'
                    ORDER BY Id LIMIT 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "AccessLevel",
                table: "AspNetUsers");
        }
    }
}
