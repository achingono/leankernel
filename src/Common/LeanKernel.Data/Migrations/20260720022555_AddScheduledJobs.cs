using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cron = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    JobType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobs", x => x.Id);
                    table.CheckConstraint("CK_ScheduledJobs_Scope", "\"TenantId\" IS NOT NULL OR \"ChannelId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_ChannelId_Name",
                table: "ScheduledJobs",
                columns: new[] { "ChannelId", "Name" },
                unique: true,
                filter: "\"TenantId\" IS NULL AND \"ChannelId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_Enabled",
                table: "ScheduledJobs",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_TenantId_ChannelId",
                table: "ScheduledJobs",
                columns: new[] { "TenantId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_TenantId_ChannelId_Name",
                table: "ScheduledJobs",
                columns: new[] { "TenantId", "ChannelId", "Name" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL AND \"ChannelId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_TenantId_Name",
                table: "ScheduledJobs",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL AND \"ChannelId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledJobs");
        }
    }
}
