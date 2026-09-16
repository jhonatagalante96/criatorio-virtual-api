using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvolveExternalGenealogyNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_genealogy_nodes_breeding_farm_id",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_genealogy_nodes_tree_bird",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "Id", "BirdId" });

            migrationBuilder.CreateTable(
                name: "external_genealogy_nodes",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenealogyRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sex = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_genealogy_nodes", x => x.Id);
                    table.UniqueConstraint("ak_external_genealogy_nodes_tree_id", x => new { x.BreedingFarmId, x.GenealogyRootId, x.Id });
                    table.CheckConstraint("ck_external_genealogy_nodes_name_not_blank", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_external_genealogy_nodes_sex_valid", "\"Sex\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_external_genealogy_nodes_genealogy_nodes_BreedingFarmId_Gen~",
                        columns: x => new { x.BreedingFarmId, x.GenealogyRootId },
                        principalSchema: "app",
                        principalTable: "genealogy_nodes",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_genealogy_parent_links",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenealogyRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildBirdId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChildExternalNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ParentBirdId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentExternalNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentSourceBreedingFarmId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentSnapshotName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParentSnapshotSex = table.Column<int>(type: "integer", nullable: true),
                    ParentSnapshotBirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ParentSnapshotRingNumber = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    ParentSnapshotStatus = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_genealogy_parent_links", x => x.Id);
                    table.CheckConstraint("ck_external_genealogy_parent_links_child_source", "(\"ChildBirdId\" IS NOT NULL AND \"ChildExternalNodeId\" IS NULL) OR (\"ChildBirdId\" IS NULL AND \"ChildExternalNodeId\" IS NOT NULL)");
                    table.CheckConstraint("ck_external_genealogy_parent_links_external_not_self", "\"ChildExternalNodeId\" IS NULL OR \"ParentExternalNodeId\" IS NULL OR \"ChildExternalNodeId\" <> \"ParentExternalNodeId\"");
                    table.CheckConstraint("ck_external_genealogy_parent_links_parent_source", "(\"ParentBirdId\" IS NOT NULL AND \"ParentExternalNodeId\" IS NULL) OR (\"ParentBirdId\" IS NULL AND \"ParentExternalNodeId\" IS NOT NULL)");
                    table.CheckConstraint("ck_external_genealogy_parent_links_position_valid", "\"Position\" IN ('father', 'mother')");
                    table.CheckConstraint("ck_external_genealogy_parent_links_snapshot_consistency", "(\"ParentBirdId\" IS NULL AND \"ParentSnapshotName\" IS NULL AND \"ParentSnapshotSex\" IS NULL AND \"ParentSnapshotBirthDate\" IS NULL AND \"ParentSnapshotRingNumber\" IS NULL AND \"ParentSnapshotStatus\" IS NULL) OR (\"ParentBirdId\" IS NOT NULL AND \"ParentSnapshotName\" IS NOT NULL AND \"ParentSnapshotSex\" IS NOT NULL AND \"ParentSnapshotStatus\" IS NOT NULL)");
                    table.CheckConstraint("ck_external_genealogy_parent_links_snapshot_ring_number_format", "\"ParentSnapshotRingNumber\" IS NULL OR \"ParentSnapshotRingNumber\" ~ '^[0-9]{6}$'");
                    table.CheckConstraint("ck_external_genealogy_parent_links_snapshot_sex_valid", "\"ParentBirdId\" IS NULL OR (\"Position\" = 'father' AND \"ParentSnapshotSex\" = 1) OR (\"Position\" = 'mother' AND \"ParentSnapshotSex\" = 2)");
                    table.CheckConstraint("ck_external_genealogy_parent_links_snapshot_status_valid", "\"ParentSnapshotStatus\" IS NULL OR \"ParentSnapshotStatus\" IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_external_genealogy_parent_links_external_genealogy_nodes_Br~",
                        columns: x => new { x.BreedingFarmId, x.GenealogyRootId, x.ChildExternalNodeId },
                        principalSchema: "app",
                        principalTable: "external_genealogy_nodes",
                        principalColumns: new[] { "BreedingFarmId", "GenealogyRootId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_genealogy_parent_links_external_genealogy_nodes_B~1",
                        columns: x => new { x.BreedingFarmId, x.GenealogyRootId, x.ParentExternalNodeId },
                        principalSchema: "app",
                        principalTable: "external_genealogy_nodes",
                        principalColumns: new[] { "BreedingFarmId", "GenealogyRootId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_genealogy_parent_links_genealogy_nodes_BreedingFar~",
                        columns: x => new { x.BreedingFarmId, x.GenealogyRootId },
                        principalSchema: "app",
                        principalTable: "genealogy_nodes",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_genealogy_nodes_tree",
                schema: "app",
                table: "external_genealogy_nodes",
                columns: new[] { "BreedingFarmId", "GenealogyRootId" });

            migrationBuilder.CreateIndex(
                name: "ix_external_genealogy_parent_links_external_parent",
                schema: "app",
                table: "external_genealogy_parent_links",
                columns: new[] { "BreedingFarmId", "GenealogyRootId", "ParentExternalNodeId" });

            migrationBuilder.CreateIndex(
                name: "ux_external_genealogy_parent_links_bird_position",
                schema: "app",
                table: "external_genealogy_parent_links",
                columns: new[] { "BreedingFarmId", "GenealogyRootId", "ChildBirdId", "Position" },
                unique: true,
                filter: "\"ChildBirdId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_external_genealogy_parent_links_external_position",
                schema: "app",
                table: "external_genealogy_parent_links",
                columns: new[] { "BreedingFarmId", "GenealogyRootId", "ChildExternalNodeId", "Position" },
                unique: true,
                filter: "\"ChildExternalNodeId\" IS NOT NULL");

            // Keep the legacy bird columns during the compatibility window, but materialize
            // every existing external parent as a stable tree-scoped node and relation. The
            // deterministic identifiers make the migration idempotent and make the Down path
            // able to distinguish migrated rows from later recursive data.
            migrationBuilder.Sql(
                """
                WITH legacy_external AS
                (
                    SELECT root."Id" AS "GenealogyRootId",
                           bird."BreedingFarmId",
                           bird."Id" AS "ChildBirdId",
                           'father'::text AS "Position",
                           btrim(bird."ExternalFatherName") AS "Name",
                           COALESCE(bird."ExternalFatherSex", 1) AS "Sex",
                           bird."CreatedAtUtc",
                           bird."UpdatedAtUtc"
                    FROM app.birds AS bird
                    INNER JOIN app.genealogy_nodes AS root
                        ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                    WHERE bird."ExternalFatherName" IS NOT NULL
                      AND btrim(bird."ExternalFatherName") <> ''
                    UNION ALL
                    SELECT root."Id",
                           bird."BreedingFarmId",
                           bird."Id",
                           'mother'::text,
                           btrim(bird."ExternalMotherName"),
                           COALESCE(bird."ExternalMotherSex", 2),
                           bird."CreatedAtUtc",
                           bird."UpdatedAtUtc"
                    FROM app.birds AS bird
                    INNER JOIN app.genealogy_nodes AS root
                        ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                    WHERE bird."ExternalMotherName" IS NOT NULL
                      AND btrim(bird."ExternalMotherName") <> ''
                )
                INSERT INTO app.external_genealogy_nodes
                    ("Id", "BreedingFarmId", "GenealogyRootId", "Name", "Sex", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT md5(legacy."GenealogyRootId"::text || ':' || legacy."Position")::uuid,
                       legacy."BreedingFarmId",
                       legacy."GenealogyRootId",
                       legacy."Name",
                       legacy."Sex",
                       legacy."CreatedAtUtc",
                       legacy."UpdatedAtUtc"
                FROM legacy_external AS legacy
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.Sql(
                """
                WITH legacy_external AS
                (
                    SELECT root."Id" AS "GenealogyRootId",
                           bird."BreedingFarmId",
                           bird."Id" AS "ChildBirdId",
                           'father'::text AS "Position"
                    FROM app.birds AS bird
                    INNER JOIN app.genealogy_nodes AS root
                        ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                    WHERE bird."ExternalFatherName" IS NOT NULL
                      AND btrim(bird."ExternalFatherName") <> ''
                    UNION ALL
                    SELECT root."Id",
                           bird."BreedingFarmId",
                           bird."Id",
                           'mother'::text
                    FROM app.birds AS bird
                    INNER JOIN app.genealogy_nodes AS root
                        ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                    WHERE bird."ExternalMotherName" IS NOT NULL
                      AND btrim(bird."ExternalMotherName") <> ''
                )
                INSERT INTO app.external_genealogy_parent_links
                    ("Id", "BreedingFarmId", "GenealogyRootId", "ChildBirdId", "Position",
                     "ParentExternalNodeId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT md5(legacy."GenealogyRootId"::text || ':link:' || legacy."Position")::uuid,
                       legacy."BreedingFarmId",
                       legacy."GenealogyRootId",
                       legacy."ChildBirdId",
                       legacy."Position",
                       md5(legacy."GenealogyRootId"::text || ':' || legacy."Position")::uuid,
                       root."CreatedAtUtc",
                       root."UpdatedAtUtc"
                FROM legacy_external AS legacy
                INNER JOIN app.genealogy_nodes AS root
                    ON root."Id" = legacy."GenealogyRootId"
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS
                    (
                        WITH expected AS
                        (
                            SELECT md5(root."Id"::text || ':father')::uuid AS "Id",
                                   bird."BreedingFarmId",
                                   root."Id" AS "GenealogyRootId",
                                   btrim(bird."ExternalFatherName") AS "Name",
                                   COALESCE(bird."ExternalFatherSex", 1) AS "Sex",
                                   bird."CreatedAtUtc",
                                   bird."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalFatherName" IS NOT NULL
                              AND btrim(bird."ExternalFatherName") <> ''
                            UNION ALL
                            SELECT md5(root."Id"::text || ':mother')::uuid,
                                   bird."BreedingFarmId",
                                   root."Id",
                                   btrim(bird."ExternalMotherName"),
                                   COALESCE(bird."ExternalMotherSex", 2),
                                   bird."CreatedAtUtc",
                                   bird."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalMotherName" IS NOT NULL
                              AND btrim(bird."ExternalMotherName") <> ''
                        )
                        SELECT 1
                        FROM app.external_genealogy_nodes AS actual
                        LEFT JOIN expected
                            ON expected."Id" = actual."Id"
                           AND expected."BreedingFarmId" = actual."BreedingFarmId"
                           AND expected."GenealogyRootId" = actual."GenealogyRootId"
                           AND expected."Name" = actual."Name"
                           AND expected."Sex" = actual."Sex"
                           AND expected."CreatedAtUtc" = actual."CreatedAtUtc"
                           AND expected."UpdatedAtUtc" = actual."UpdatedAtUtc"
                        WHERE expected."Id" IS NULL
                    )
                    OR EXISTS
                    (
                        WITH expected AS
                        (
                            SELECT md5(root."Id"::text || ':father')::uuid AS "Id",
                                   bird."BreedingFarmId",
                                   root."Id" AS "GenealogyRootId",
                                   btrim(bird."ExternalFatherName") AS "Name",
                                   COALESCE(bird."ExternalFatherSex", 1) AS "Sex",
                                   bird."CreatedAtUtc",
                                   bird."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalFatherName" IS NOT NULL
                              AND btrim(bird."ExternalFatherName") <> ''
                            UNION ALL
                            SELECT md5(root."Id"::text || ':mother')::uuid,
                                   bird."BreedingFarmId",
                                   root."Id",
                                   btrim(bird."ExternalMotherName"),
                                   COALESCE(bird."ExternalMotherSex", 2),
                                   bird."CreatedAtUtc",
                                   bird."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalMotherName" IS NOT NULL
                              AND btrim(bird."ExternalMotherName") <> ''
                        )
                        SELECT 1
                        FROM expected
                        LEFT JOIN app.external_genealogy_nodes AS actual
                            ON expected."Id" = actual."Id"
                           AND expected."BreedingFarmId" = actual."BreedingFarmId"
                           AND expected."GenealogyRootId" = actual."GenealogyRootId"
                           AND expected."Name" = actual."Name"
                           AND expected."Sex" = actual."Sex"
                           AND expected."CreatedAtUtc" = actual."CreatedAtUtc"
                           AND expected."UpdatedAtUtc" = actual."UpdatedAtUtc"
                        WHERE actual."Id" IS NULL
                    )
                    OR EXISTS
                    (
                        WITH expected AS
                        (
                            SELECT md5(root."Id"::text || ':link:father')::uuid AS "Id",
                                   bird."BreedingFarmId",
                                   root."Id" AS "GenealogyRootId",
                                   bird."Id" AS "ChildBirdId",
                                   'father'::text AS "Position",
                                   md5(root."Id"::text || ':father')::uuid AS "ParentExternalNodeId",
                                   root."CreatedAtUtc",
                                   root."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalFatherName" IS NOT NULL
                              AND btrim(bird."ExternalFatherName") <> ''
                            UNION ALL
                            SELECT md5(root."Id"::text || ':link:mother')::uuid,
                                   bird."BreedingFarmId",
                                   root."Id",
                                   bird."Id",
                                   'mother'::text,
                                   md5(root."Id"::text || ':mother')::uuid,
                                   root."CreatedAtUtc",
                                   root."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalMotherName" IS NOT NULL
                              AND btrim(bird."ExternalMotherName") <> ''
                        )
                        SELECT 1
                        FROM app.external_genealogy_parent_links AS actual
                        LEFT JOIN expected
                            ON expected."Id" = actual."Id"
                           AND expected."BreedingFarmId" = actual."BreedingFarmId"
                           AND expected."GenealogyRootId" = actual."GenealogyRootId"
                           AND expected."ChildBirdId" = actual."ChildBirdId"
                           AND expected."Position" = actual."Position"
                           AND expected."ParentExternalNodeId" = actual."ParentExternalNodeId"
                           AND expected."CreatedAtUtc" = actual."CreatedAtUtc"
                           AND expected."UpdatedAtUtc" = actual."UpdatedAtUtc"
                        WHERE expected."Id" IS NULL
                    )
                    OR EXISTS
                    (
                        WITH expected AS
                        (
                            SELECT md5(root."Id"::text || ':link:father')::uuid AS "Id",
                                   bird."BreedingFarmId",
                                   root."Id" AS "GenealogyRootId",
                                   bird."Id" AS "ChildBirdId",
                                   'father'::text AS "Position",
                                   md5(root."Id"::text || ':father')::uuid AS "ParentExternalNodeId",
                                   root."CreatedAtUtc",
                                   root."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalFatherName" IS NOT NULL
                              AND btrim(bird."ExternalFatherName") <> ''
                            UNION ALL
                            SELECT md5(root."Id"::text || ':link:mother')::uuid,
                                   bird."BreedingFarmId",
                                   root."Id",
                                   bird."Id",
                                   'mother'::text,
                                   md5(root."Id"::text || ':mother')::uuid,
                                   root."CreatedAtUtc",
                                   root."UpdatedAtUtc"
                            FROM app.birds AS bird
                            INNER JOIN app.genealogy_nodes AS root
                                ON root."BirdId" = bird."Id" AND root."IsRoot" = TRUE
                            WHERE bird."ExternalMotherName" IS NOT NULL
                              AND btrim(bird."ExternalMotherName") <> ''
                        )
                        SELECT 1
                        FROM expected
                        LEFT JOIN app.external_genealogy_parent_links AS actual
                            ON expected."Id" = actual."Id"
                           AND expected."BreedingFarmId" = actual."BreedingFarmId"
                           AND expected."GenealogyRootId" = actual."GenealogyRootId"
                           AND expected."ChildBirdId" = actual."ChildBirdId"
                           AND expected."Position" = actual."Position"
                           AND expected."ParentExternalNodeId" = actual."ParentExternalNodeId"
                           AND expected."CreatedAtUtc" = actual."CreatedAtUtc"
                           AND expected."UpdatedAtUtc" = actual."UpdatedAtUtc"
                        WHERE actual."Id" IS NULL
                    )
                    THEN
                        RAISE EXCEPTION 'Cannot roll back external genealogy migration without losing recursive nodes.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "external_genealogy_parent_links",
                schema: "app");

            migrationBuilder.DropTable(
                name: "external_genealogy_nodes",
                schema: "app");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_genealogy_nodes_breeding_farm_id",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_genealogy_nodes_tree_bird",
                schema: "app",
                table: "genealogy_nodes");
        }
    }
}
