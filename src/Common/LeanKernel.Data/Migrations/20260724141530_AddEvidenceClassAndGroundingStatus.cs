using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceClassAndGroundingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EvidenceClass",
                table: "TurnTelemetry",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroundingStatus",
                table: "TurnTelemetry",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RetrievedEvidenceClassesJson",
                table: "TurnTelemetry",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetrievedMemoryKeysJson",
                table: "TurnTelemetry",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentIngestionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailabilityScope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIngestionJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_EvidenceClass",
                table: "TurnTelemetry",
                column: "EvidenceClass");

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_GroundingStatus",
                table: "TurnTelemetry",
                column: "GroundingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIngestionJobs_TenantId_Status_NextAttemptAt_LeaseEx~",
                table: "DocumentIngestionJobs",
                columns: new[] { "TenantId", "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIngestionJobs_TenantId_UserId_ChannelId",
                table: "DocumentIngestionJobs",
                columns: new[] { "TenantId", "UserId", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentIngestionJobs");

            migrationBuilder.DropIndex(
                name: "IX_TurnTelemetry_EvidenceClass",
                table: "TurnTelemetry");

            migrationBuilder.DropIndex(
                name: "IX_TurnTelemetry_GroundingStatus",
                table: "TurnTelemetry");

            migrationBuilder.DropColumn(
                name: "EvidenceClass",
                table: "TurnTelemetry");

            migrationBuilder.DropColumn(
                name: "GroundingStatus",
                table: "TurnTelemetry");

            migrationBuilder.DropColumn(
                name: "RetrievedEvidenceClassesJson",
                table: "TurnTelemetry");

            migrationBuilder.DropColumn(
                name: "RetrievedMemoryKeysJson",
                table: "TurnTelemetry");
        }
    }
}
