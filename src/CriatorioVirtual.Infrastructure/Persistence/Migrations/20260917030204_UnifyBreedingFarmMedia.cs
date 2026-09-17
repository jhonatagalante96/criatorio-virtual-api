using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyBreedingFarmMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Caption",
                schema: "app",
                table: "bird_attachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bird_attachments_farm_created_media",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "CreatedAtUtc", "Id" },
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_attachments_caption_not_blank",
                schema: "app",
                table: "bird_attachments",
                sql: "\"Caption\" IS NULL OR btrim(\"Caption\") <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_attachments_caption_not_blank",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.DropIndex(
                name: "ix_bird_attachments_farm_created_media",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.DropColumn(
                name: "Caption",
                schema: "app",
                table: "bird_attachments");
        }
    }
}
