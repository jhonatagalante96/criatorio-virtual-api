using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarContentType",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarObjectKey",
                schema: "identity",
                table: "users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_avatar_reference_consistent",
                schema: "identity",
                table: "users",
                sql: "(\"AvatarObjectKey\" IS NULL AND \"AvatarContentType\" IS NULL) OR (\"AvatarObjectKey\" IS NOT NULL AND btrim(\"AvatarObjectKey\") <> '' AND \"AvatarContentType\" IN ('image/jpeg', 'image/png'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_avatar_reference_consistent",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AvatarContentType",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AvatarObjectKey",
                schema: "identity",
                table: "users");
        }
    }
}
