using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.Id);
                    table.CheckConstraint("ck_payment_attempts_request_fingerprint_valid", "\"RequestFingerprint\" ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("ck_payment_attempts_status_valid", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_payment_attempts_idempotency_key_not_empty", "\"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_payment_attempts_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "app",
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_payment_status",
                schema: "app",
                table: "payment_attempts",
                columns: new[] { "PaymentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_payment_idempotency_key",
                schema: "app",
                table: "payment_attempts",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "app");
        }
    }
}
