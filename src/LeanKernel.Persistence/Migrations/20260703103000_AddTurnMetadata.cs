using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Persistence.Migrations
{
    /// <summary>
    /// Adds a nullable JSON metadata column to persisted conversation turns.
    /// </summary>
    public partial class AddTurnMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                schema: "engine",
                table: "Turns",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                schema: "engine",
                table: "Turns");
        }
    }
}
