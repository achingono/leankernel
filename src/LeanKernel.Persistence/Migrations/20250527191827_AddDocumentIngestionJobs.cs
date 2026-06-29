using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Persistence.Migrations
{
    /// <summary>
    /// Adds the document ingestion jobs table and supporting indexes.
    /// </summary>
    /// <inheritdoc />
    public partial class AddDocumentIngestionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentIngestionJobs",
                schema: "engine",
                columns: table => new
                {
                    JobId = table.Column<string>(type: "text", nullable: false),
                    Filename = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIngestionJobs", x => x.JobId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIngestionJobs_CompletedAt",
                schema: "engine",
                table: "DocumentIngestionJobs",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIngestionJobs_CreatedAt",
                schema: "engine",
                table: "DocumentIngestionJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIngestionJobs_Status",
                schema: "engine",
                table: "DocumentIngestionJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentIngestionJobs",
                schema: "engine");
        }
    }
}
