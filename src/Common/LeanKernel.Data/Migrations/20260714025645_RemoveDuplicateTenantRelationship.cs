using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDuplicateTenantRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Tenants_TenantEntityId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_TenantEntityId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TenantEntityId",
                table: "Sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantEntityId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TenantEntityId",
                table: "Sessions",
                column: "TenantEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Tenants_TenantEntityId",
                table: "Sessions",
                column: "TenantEntityId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }
    }
}