using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefineDocumentContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bird_documents",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByBreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ModelId = table.Column<int>(type: "integer", nullable: true),
                    PrintSize = table.Column<int>(type: "integer", nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SelectedFieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bird_documents", x => x.Id);
                    table.CheckConstraint("ck_bird_documents_content_type_not_blank", "btrim(\"ContentType\") <> ''");
                    table.CheckConstraint("ck_bird_documents_content_type_pdf", "lower(\"ContentType\") = 'application/pdf'");
                    table.CheckConstraint("ck_bird_documents_file_name_not_blank", "btrim(\"FileName\") <> ''");
                    table.CheckConstraint("ck_bird_documents_length_positive", "\"Length\" > 0");
                    table.CheckConstraint("ck_bird_documents_model_size_consistency", "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 2 AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");
                    table.CheckConstraint("ck_bird_documents_model_valid", "\"ModelId\" IS NULL OR \"ModelId\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_bird_documents_object_key_not_blank", "btrim(\"ObjectKey\") <> ''");
                    table.CheckConstraint("ck_bird_documents_print_size_valid", "\"PrintSize\" IS NULL OR \"PrintSize\" IN (1, 2, 3)");
                    table.CheckConstraint("ck_bird_documents_selected_fields_array", "jsonb_typeof(\"SelectedFieldsJson\") = 'array'");
                    table.CheckConstraint("ck_bird_documents_snapshot_object", "jsonb_typeof(\"SnapshotJson\") = 'object'");
                    table.CheckConstraint("ck_bird_documents_type_valid", "\"Type\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_bird_documents_birds_BirdId",
                        column: x => x.BirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bird_documents_breeding_farms_CreatedByBreedingFarmId",
                        column: x => x.CreatedByBreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bird_documents_bird_generated_at",
                schema: "app",
                table: "bird_documents",
                columns: new[] { "BirdId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_bird_documents_provenance_created_at",
                schema: "app",
                table: "bird_documents",
                columns: new[] { "CreatedByBreedingFarmId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_bird_documents_provenance_object_key",
                schema: "app",
                table: "bird_documents",
                columns: new[] { "CreatedByBreedingFarmId", "ObjectKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bird_documents",
                schema: "app");
        }
    }
}
