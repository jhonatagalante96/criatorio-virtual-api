using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiveAsaasWebhookInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asaas_webhook_events",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asaas_webhook_events", x => x.Id);
                    table.CheckConstraint("ck_asaas_webhook_events_event_type_not_blank", "btrim(\"EventType\") <> ''");
                    table.CheckConstraint("ck_asaas_webhook_events_payload_object", "jsonb_typeof(\"Payload\") = 'object'");
                    table.CheckConstraint("ck_asaas_webhook_events_provider_event_id_not_blank", "btrim(\"ProviderEventId\") <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ux_asaas_webhook_events_provider_event_id",
                schema: "app",
                table: "asaas_webhook_events",
                column: "ProviderEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asaas_webhook_events",
                schema: "app");
        }
    }
}
