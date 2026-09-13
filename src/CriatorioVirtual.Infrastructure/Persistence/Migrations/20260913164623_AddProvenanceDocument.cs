using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvenanceDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_type_valid",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents",
                sql: "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" IN (3, 4) AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_type_valid",
                schema: "app",
                table: "bird_documents",
                sql: "\"Type\" IN (1, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_type_valid",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents",
                sql: "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 3 AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_type_valid",
                schema: "app",
                table: "bird_documents",
                sql: "\"Type\" IN (1, 3)");
        }
    }
}
