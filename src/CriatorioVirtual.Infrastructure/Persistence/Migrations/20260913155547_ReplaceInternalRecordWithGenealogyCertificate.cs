using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceInternalRecordWithGenealogyCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM app.bird_documents WHERE "Type" = 2) THEN
                        RAISE EXCEPTION 'Cannot replace InternalRecord while legacy bird documents still exist.';
                    END IF;
                END $$;
                """);

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
                sql: "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 2 AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_type_valid",
                schema: "app",
                table: "bird_documents",
                sql: "\"Type\" IN (1, 2)");
        }
    }
}
