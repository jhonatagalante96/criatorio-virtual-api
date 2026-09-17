using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetryAsaasWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                schema: "app",
                table: "asaas_webhook_events",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAtUtc",
                schema: "app",
                table: "asaas_webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                schema: "app",
                table: "asaas_webhook_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_asaas_webhook_events_retry",
                schema: "app",
                table: "asaas_webhook_events",
                columns: new[] { "NextAttemptAtUtc", "ReceivedAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asaas_webhook_events_retry",
                schema: "app",
                table: "asaas_webhook_events");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "app",
                table: "asaas_webhook_events");

            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                schema: "app",
                table: "asaas_webhook_events");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                schema: "app",
                table: "asaas_webhook_events");
        }
    }
}
