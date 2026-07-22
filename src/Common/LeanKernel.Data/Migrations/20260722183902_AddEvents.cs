using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CausationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_CorrelationId",
                table: "Events",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventId",
                table: "Events",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventType",
                table: "Events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_Timestamp",
                table: "Events",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_UserId_ChannelId_Timestamp",
                table: "Events",
                columns: new[] { "TenantId", "UserId", "ChannelId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
