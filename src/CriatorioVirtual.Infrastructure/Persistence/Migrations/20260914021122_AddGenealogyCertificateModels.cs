using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenealogyCertificateModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.AddColumn<int>(
                name: "CertificateModelId",
                schema: "app",
                table: "bird_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE app.bird_documents SET \"CertificateModelId\" = 2 WHERE \"Type\" = 3 AND \"CertificateModelId\" IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_certificate_model_valid",
                schema: "app",
                table: "bird_documents",
                sql: "\"CertificateModelId\" IS NULL OR \"CertificateModelId\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents",
                sql: "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"CertificateModelId\" IS NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 3 AND \"ModelId\" IS NULL AND \"CertificateModelId\" IS NOT NULL AND \"PrintSize\" IS NULL) OR (\"Type\" = 4 AND \"ModelId\" IS NULL AND \"CertificateModelId\" IS NULL AND \"PrintSize\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_certificate_model_valid",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.DropColumn(
                name: "CertificateModelId",
                schema: "app",
                table: "bird_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_documents_model_size_consistency",
                schema: "app",
                table: "bird_documents",
                sql: "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" IN (3, 4) AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");
        }
    }
}
