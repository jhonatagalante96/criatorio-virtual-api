using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalParentSex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExternalFatherSex",
                schema: "app",
                table: "birds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalMotherSex",
                schema: "app",
                table: "birds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_birds_external_father_sex",
                schema: "app",
                table: "birds",
                sql: "\"ExternalFatherSex\" IS NULL OR (\"ExternalFatherName\" IS NOT NULL AND \"ExternalFatherSex\" = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_birds_external_mother_sex",
                schema: "app",
                table: "birds",
                sql: "\"ExternalMotherSex\" IS NULL OR (\"ExternalMotherName\" IS NOT NULL AND \"ExternalMotherSex\" = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_birds_external_father_sex",
                schema: "app",
                table: "birds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_birds_external_mother_sex",
                schema: "app",
                table: "birds");

            migrationBuilder.DropColumn(
                name: "ExternalFatherSex",
                schema: "app",
                table: "birds");

            migrationBuilder.DropColumn(
                name: "ExternalMotherSex",
                schema: "app",
                table: "birds");
        }
    }
}
