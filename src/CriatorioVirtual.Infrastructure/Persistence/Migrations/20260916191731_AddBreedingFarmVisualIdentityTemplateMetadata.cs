using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmVisualIdentityTemplateMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityTemplateConfiguration",
                schema: "app",
                table: "breeding_farms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityTemplateModelId",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityTemplateVersion",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL AND \"VisualIdentityTemplateModelId\" IS NULL AND \"VisualIdentityTemplateVersion\" IS NULL AND \"VisualIdentityTemplateConfiguration\" IS NULL) OR (\"VisualIdentitySource\" = 1 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" IN ('image/jpeg', 'image/png') AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760 AND \"VisualIdentityTemplateModelId\" IS NULL AND \"VisualIdentityTemplateVersion\" IS NULL AND \"VisualIdentityTemplateConfiguration\" IS NULL) OR (\"VisualIdentitySource\" = 2 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" = 'image/png' AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760 AND \"VisualIdentityTemplateModelId\" IS NOT NULL AND btrim(\"VisualIdentityTemplateModelId\") <> '' AND \"VisualIdentityTemplateVersion\" IS NOT NULL AND btrim(\"VisualIdentityTemplateVersion\") <> '' AND \"VisualIdentityTemplateConfiguration\" IS NOT NULL AND jsonb_typeof(\"VisualIdentityTemplateConfiguration\") = 'object' AND jsonb_typeof(\"VisualIdentityTemplateConfiguration\" -> 'variant') = 'string' AND btrim(\"VisualIdentityTemplateConfiguration\" ->> 'variant') <> '' AND \"VisualIdentityTemplateConfiguration\" = jsonb_build_object('variant', \"VisualIdentityTemplateConfiguration\" -> 'variant'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityTemplateConfiguration",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityTemplateModelId",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityTemplateVersion",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL) OR (\"VisualIdentitySource\" = 1 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" IN ('image/jpeg', 'image/png') AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760) OR (\"VisualIdentitySource\" = 2 AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL)");
        }
    }
}
