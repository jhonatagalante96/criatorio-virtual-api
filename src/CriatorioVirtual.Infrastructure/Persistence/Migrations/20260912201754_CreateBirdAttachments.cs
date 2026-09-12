using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBirdAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bird_attachments",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bird_attachments", x => x.Id);
                    table.CheckConstraint("ck_bird_attachments_content_type_not_blank", "btrim(\"ContentType\") <> ''");
                    table.CheckConstraint("ck_bird_attachments_file_name_not_blank", "btrim(\"FileName\") <> ''");
                    table.CheckConstraint("ck_bird_attachments_length_positive", "\"Length\" > 0");
                    table.CheckConstraint("ck_bird_attachments_object_key_not_blank", "btrim(\"ObjectKey\") <> ''");
                    table.ForeignKey(
                        name: "FK_bird_attachments_birds_BreedingFarmId_BirdId",
                        columns: x => new { x.BreedingFarmId, x.BirdId },
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bird_attachments_farm_bird_created_at",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "BirdId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_bird_attachments_farm_object_key",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "ObjectKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bird_attachments",
                schema: "app");
        }
    }
}
