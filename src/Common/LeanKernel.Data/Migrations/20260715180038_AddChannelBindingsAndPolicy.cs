using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelBindingsAndPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelMemoryPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareList = table.Column<string>(type: "text", nullable: false),
                    AccessList = table.Column<string>(type: "text", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMemoryPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelMemoryPolicies_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelMemoryPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSenderBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSenderBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelSenderBindings_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelSenderBindings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelSenderBindings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemoryPolicies_ChannelId",
                table: "ChannelMemoryPolicies",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemoryPolicies_TenantId_ChannelId",
                table: "ChannelMemoryPolicies",
                columns: new[] { "TenantId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSenderBindings_ChannelId",
                table: "ChannelSenderBindings",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSenderBindings_TenantId_ChannelId_Issuer_Subject",
                table: "ChannelSenderBindings",
                columns: new[] { "TenantId", "ChannelId", "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSenderBindings_TenantId_ChannelId_UserId",
                table: "ChannelSenderBindings",
                columns: new[] { "TenantId", "ChannelId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSenderBindings_UserId",
                table: "ChannelSenderBindings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelMemoryPolicies");

            migrationBuilder.DropTable(
                name: "ChannelSenderBindings");
        }
    }
}
