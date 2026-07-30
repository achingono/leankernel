using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticEntries_CapturedAt",
                table: "DiagnosticEntries",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticEntries_CorrelationId",
                table: "DiagnosticEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticEntries_TurnId",
                table: "DiagnosticEntries",
                column: "TurnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticEntries");
        }
    }
}