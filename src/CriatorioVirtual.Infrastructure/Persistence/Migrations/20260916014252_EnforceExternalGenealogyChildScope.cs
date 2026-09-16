using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceExternalGenealogyChildScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_external_genealogy_parent_links_genealogy_nodes_BreedingFa~1",
                schema: "app",
                table: "external_genealogy_parent_links",
                columns: new[] { "BreedingFarmId", "GenealogyRootId", "ChildBirdId" },
                principalSchema: "app",
                principalTable: "genealogy_nodes",
                principalColumns: new[] { "BreedingFarmId", "Id", "BirdId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_external_genealogy_parent_links_genealogy_nodes_BreedingFa~1",
                schema: "app",
                table: "external_genealogy_parent_links");
        }
    }
}
