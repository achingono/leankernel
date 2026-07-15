using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelBindingBearerToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BearerToken",
                table: "ChannelSenderBindings",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BearerToken",
                table: "ChannelSenderBindings");
        }
    }
}
