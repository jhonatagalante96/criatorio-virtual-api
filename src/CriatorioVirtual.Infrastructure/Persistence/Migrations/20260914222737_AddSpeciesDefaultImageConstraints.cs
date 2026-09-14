using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeciesDefaultImageConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_species_default_image_metadata_pair",
                schema: "app",
                table: "species",
                sql: "(\"DefaultImageFileName\" IS NULL) = (\"DefaultImageContentType\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_birds_default_image_metadata_pair",
                schema: "app",
                table: "birds",
                sql: "(\"DefaultImageFileName\" IS NULL) = (\"DefaultImageContentType\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_species_default_image_metadata_pair",
                schema: "app",
                table: "species");

            migrationBuilder.DropCheckConstraint(
                name: "ck_birds_default_image_metadata_pair",
                schema: "app",
                table: "birds");
        }
    }
}
