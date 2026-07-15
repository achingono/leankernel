using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelMemoryPolicies : Migration
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
                    Share = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Access = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMemoryPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelMemoryPolicies_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelMemoryPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelMemoryPolicies");
        }
    }
}
