using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeciesDefaultImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultImageContentType",
                schema: "app",
                table: "species",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultImageFileName",
                schema: "app",
                table: "species",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultImageContentType",
                schema: "app",
                table: "birds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultImageFileName",
                schema: "app",
                table: "birds",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0001.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0002.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0003.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0004.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0005.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0006.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000007"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0007.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000008"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0008.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000009"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0009.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000010"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0010.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0011.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0012.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0013.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000014"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0014.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000015"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0015.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000016"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0016.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000017"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0017.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000018"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0018.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000019"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0019.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000020"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0020.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000021"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0021.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000022"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0022.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000023"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0023.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000024"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0024.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000025"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0025.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000026"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0026.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000027"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0027.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000028"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0028.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000029"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0029.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000030"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0030.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000031"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0031.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000032"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0032.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000033"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0033.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000034"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0034.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000035"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0035.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000036"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0036.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000037"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0037.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000038"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0038.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000039"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0039.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000040"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0040.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000041"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0041.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000042"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0042.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000043"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0043.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000044"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0044.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000045"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0045.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000046"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0046.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000047"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0047.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000048"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0048.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000049"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0049.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000050"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0050.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000051"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0051.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000052"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0052.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000053"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0053.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000054"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0054.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000055"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0055.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000056"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0056.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000057"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0057.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000058"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0058.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000059"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0059.jpg" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "species",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000060"),
                columns: new[] { "DefaultImageContentType", "DefaultImageFileName" },
                values: new object[] { "image/jpeg", "0060.jpg" });

            migrationBuilder.Sql("""
                UPDATE app.birds AS bird
                SET "DefaultImageFileName" = species."DefaultImageFileName",
                    "DefaultImageContentType" = species."DefaultImageContentType"
                FROM app.species AS species
                WHERE bird."SpeciesId" = species."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultImageContentType",
                schema: "app",
                table: "species");

            migrationBuilder.DropColumn(
                name: "DefaultImageFileName",
                schema: "app",
                table: "species");

            migrationBuilder.DropColumn(
                name: "DefaultImageContentType",
                schema: "app",
                table: "birds");

            migrationBuilder.DropColumn(
                name: "DefaultImageFileName",
                schema: "app",
                table: "birds");
        }
    }
}
