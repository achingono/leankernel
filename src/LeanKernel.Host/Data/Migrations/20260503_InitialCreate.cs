using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeanKernel.Host.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueuedMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsUrgent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDelivered = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueuedMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueuedMessages_DeliveredAt",
                table: "QueuedMessages",
                column: "DeliveredAt");

            migrationBuilder.CreateIndex(
                name: "IX_QueuedMessages_IsDelivered_EnqueuedAt",
                table: "QueuedMessages",
                columns: new[] { "IsDelivered", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueuedMessages_IsUrgent_IsDelivered",
                table: "QueuedMessages",
                columns: new[] { "IsUrgent", "IsDelivered" });

            migrationBuilder.CreateIndex(
                name: "IX_QueuedMessages_NextRetryAt",
                table: "QueuedMessages",
                column: "NextRetryAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueuedMessages");
        }
    }
}
