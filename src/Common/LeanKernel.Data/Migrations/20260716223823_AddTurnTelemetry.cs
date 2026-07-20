using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TurnTelemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: false),
                    RequestedModel = table.Column<string>(type: "text", nullable: true),
                    ServedModel = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    ModelId = table.Column<string>(type: "text", nullable: true),
                    ApiBase = table.Column<string>(type: "text", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    ResponseCost = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CostIsEstimated = table.Column<bool>(type: "boolean", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy_Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedBy_FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedBy_Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy_Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy_FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy_Id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnTelemetry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TurnTelemetry_Turns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "Turns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_CapturedAt",
                table: "TurnTelemetry",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_Provider",
                table: "TurnTelemetry",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_ServedModel",
                table: "TurnTelemetry",
                column: "ServedModel");

            migrationBuilder.CreateIndex(
                name: "IX_TurnTelemetry_TurnId",
                table: "TurnTelemetry",
                column: "TurnId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurnTelemetry");
        }
    }
}