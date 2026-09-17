using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "breeding_farm_gallery_images",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StorageCleanupPending = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_breeding_farm_gallery_images", x => x.Id);
                    table.CheckConstraint("ck_breeding_farm_gallery_images_caption_not_blank", "\"Caption\" IS NULL OR btrim(\"Caption\") <> ''");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_cleanup_requires_deletion", "\"StorageCleanupPending\" = FALSE OR \"DeletedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_content_type_supported", "lower(\"ContentType\") IN ('image/jpeg', 'image/png', 'image/webp')");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_dimensions_supported", "\"Width\" > 0 AND \"Height\" > 0 AND \"Width\" <= 8192 AND \"Height\" <= 8192 AND \"Width\"::bigint * \"Height\"::bigint <= 40000000");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_file_length_supported", "\"Length\" > 0 AND \"Length\" <= 8388608");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_file_name_not_blank", "btrim(\"FileName\") <> ''");
                    table.CheckConstraint("ck_breeding_farm_gallery_images_object_key_not_blank", "btrim(\"ObjectKey\") <> ''");
                    table.ForeignKey(
                        name: "FK_breeding_farm_gallery_images_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_breeding_farm_gallery_images_farm_created",
                schema: "app",
                table: "breeding_farm_gallery_images",
                columns: new[] { "BreedingFarmId", "CreatedAtUtc", "Id" },
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_breeding_farm_gallery_images_farm_object_key",
                schema: "app",
                table: "breeding_farm_gallery_images",
                columns: new[] { "BreedingFarmId", "ObjectKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "breeding_farm_gallery_images",
                schema: "app");

        }
    }
}
