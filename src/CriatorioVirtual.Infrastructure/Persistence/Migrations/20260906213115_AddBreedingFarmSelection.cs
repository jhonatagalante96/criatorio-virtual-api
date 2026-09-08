using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedBreedingFarmId",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_SelectedBreedingFarmId",
                schema: "identity",
                table: "users",
                column: "SelectedBreedingFarmId");

            migrationBuilder.AddForeignKey(
                name: "FK_users_breeding_farms_SelectedBreedingFarmId",
                schema: "identity",
                table: "users",
                column: "SelectedBreedingFarmId",
                principalSchema: "app",
                principalTable: "breeding_farms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_breeding_farms_SelectedBreedingFarmId",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_SelectedBreedingFarmId",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SelectedBreedingFarmId",
                schema: "identity",
                table: "users");
        }
    }
}
