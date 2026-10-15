using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdministrationRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "AuditLogs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AuditLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "operation");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCategory",
                table: "AuditLogs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "AuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "AuditLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "succeeded");

            migrationBuilder.AddColumn<string>(
                name: "SourceIp",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "AuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoleDefinitions",
                columns: table => new
                {
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    LegacyUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDefinitions", x => x.RoleId);
                    table.ForeignKey(
                        name: "FK_RoleDefinitions_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleDefinitions_AspNetUsers_LegacyUserId",
                        column: x => x.LegacyUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    Permission = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Category_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "Category", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinitions_LegacyUserId",
                table: "RoleDefinitions",
                column: "LegacyUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_Permission_RoleId",
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" });

            // Defaults run only in this versioned migration. Starting the app or
            // running migrate again never repairs intentionally empty role grants.
            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                SELECT md5('keyvalidation:builtin:' || name)::uuid::text, name, upper(name), md5(random()::text)
                FROM (VALUES ('Admin'), ('Operator'), ('ReadOnly')) AS seed(name)
                WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" r WHERE r."NormalizedName" = upper(name));

                INSERT INTO "RoleDefinitions" ("RoleId", "Description", "IsBuiltIn", "LegacyUserId")
                SELECT "Id", CASE "NormalizedName" WHEN 'ADMIN' THEN '内置管理员，拥有全部权限且受保护。'
                    WHEN 'OPERATOR' THEN '内置操作员，保留原默认业务权限。'
                    WHEN 'READONLY' THEN '内置只读角色，保留原默认查询权限。' ELSE NULL END,
                    coalesce("NormalizedName" IN ('ADMIN', 'OPERATOR', 'READONLY'), false), NULL
                FROM "AspNetRoles";

                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", permission
                FROM "AspNetRoles" r CROSS JOIN LATERAL unnest(CASE r."NormalizedName"
                    WHEN 'ADMIN' THEN ARRAY['licenses.read','licenses.issue','licenses.manage','reports.read','audit.read','risk.review','customers.manage','orders.manage','integrations.manage','platform.manage']
                    WHEN 'OPERATOR' THEN ARRAY['licenses.read','licenses.issue','licenses.manage','reports.read','audit.read','risk.review','customers.manage','orders.manage']
                    WHEN 'READONLY' THEN ARRAY['licenses.read','reports.read','audit.read']
                    ELSE ARRAY[]::text[] END) AS grants(permission);

                CREATE TEMPORARY TABLE "LegacyRbacConversion" ON COMMIT DROP AS
                SELECT u."Id" AS "UserId", md5('keyvalidation:legacy:' || u."Id")::uuid::text AS "RoleId",
                    '历史权限 · ' || left(coalesce(u."UserName", u."Id"), 20) || ' · ' || replace(u."Id", '-', '') AS "Name",
                    ARRAY(SELECT DISTINCT permission FROM unnest(u."AccessPermissions") AS old(permission)
                        WHERE permission = ANY(CASE
                            WHEN EXISTS (SELECT 1 FROM "AspNetUserRoles" m JOIN "AspNetRoles" r ON r."Id" = m."RoleId" WHERE m."UserId" = u."Id" AND r."Name" = 'Operator')
                                THEN ARRAY['licenses.read','licenses.issue','licenses.manage','reports.read','audit.read','risk.review','customers.manage','orders.manage']
                            WHEN EXISTS (SELECT 1 FROM "AspNetUserRoles" m JOIN "AspNetRoles" r ON r."Id" = m."RoleId" WHERE m."UserId" = u."Id" AND r."Name" = 'ReadOnly')
                                THEN ARRAY['licenses.read','reports.read','audit.read']
                            ELSE ARRAY[]::text[] END) ORDER BY permission) AS "Permissions"
                FROM "AspNetUsers" u WHERE u."AccessConfigured"
                    AND NOT EXISTS (SELECT 1 FROM "AspNetUserRoles" m JOIN "AspNetRoles" r ON r."Id" = m."RoleId" WHERE m."UserId" = u."Id" AND r."Name" = 'Admin');

                -- Down retains Identity roles. Runtime legacy adapters can also
                -- leave the same per-user name with a nondeterministic role ID.
                UPDATE "LegacyRbacConversion" conversion SET "RoleId" = existing."Id"
                FROM "AspNetRoles" existing
                WHERE existing."NormalizedName" = upper(conversion."Name")
                    AND NOT EXISTS (SELECT 1 FROM "AspNetRoles" r WHERE r."Id" = conversion."RoleId");

                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                SELECT "RoleId", "Name", upper("Name"), md5(random()::text) FROM "LegacyRbacConversion"
                ON CONFLICT ("Id") DO NOTHING;
                INSERT INTO "RoleDefinitions" ("RoleId", "Description", "IsBuiltIn", "LegacyUserId")
                SELECT "RoleId", '迁移自旧版账号权限；已保留当时实际权限和数据范围。', false, "UserId" FROM "LegacyRbacConversion"
                ON CONFLICT ("RoleId") DO UPDATE SET "Description" = EXCLUDED."Description", "IsBuiltIn" = EXCLUDED."IsBuiltIn", "LegacyUserId" = EXCLUDED."LegacyUserId";
                DELETE FROM "RolePermissions" WHERE "RoleId" IN (SELECT "RoleId" FROM "LegacyRbacConversion");
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT "RoleId", unnest("Permissions") FROM "LegacyRbacConversion";
                DELETE FROM "AspNetUserRoles" WHERE "UserId" IN (SELECT "UserId" FROM "LegacyRbacConversion");
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") SELECT "UserId", "RoleId" FROM "LegacyRbacConversion";

                -- Old unconfigured/Admin scopes were always unrestricted. Other
                -- configured application/channel scopes remain byte-for-byte intact.
                UPDATE "AspNetUsers" u SET "AccessAllApplications" = true, "AccessApplicationIds" = ARRAY[]::uuid[], "AccessChannelId" = NULL
                WHERE NOT u."AccessConfigured" OR EXISTS (SELECT 1 FROM "AspNetUserRoles" m JOIN "AspNetRoles" r ON r."Id" = m."RoleId" WHERE m."UserId" = u."Id" AND r."Name" = 'Admin');
                UPDATE "AspNetUsers" SET "AccessPermissions" = ARRAY[]::text[];
                UPDATE "AspNetUsers" SET "SecurityStamp" = md5(random()::text || "Id") WHERE "Id" IN (SELECT "UserId" FROM "LegacyRbacConversion");

                UPDATE "AuditLogs" SET "Category" = 'login' WHERE "Action" IN ('security.login','security.login-two-factor','security.login-failed','security.logout');
                UPDATE "AuditLogs" SET "Result" = 'submitted' WHERE "Action" IN ('backup.create','backup.verify','webhook.retry');
                UPDATE "AuditLogs" SET "Result" = 'failed' WHERE "Action" LIKE '%-failed';
                UPDATE "AuditLogs" SET "Method" = 'password' WHERE "Action" = 'security.login';
                DO $audit_outcomes$
                DECLARE historical record;
                BEGIN
                    FOR historical IN SELECT "Id", "Changes" FROM "AuditLogs" WHERE "Action" LIKE 'license.%'
                    LOOP
                        BEGIN
                            IF historical."Changes"::jsonb @> '{"success":false}'::jsonb THEN
                                UPDATE "AuditLogs" SET "Result" = 'failed' WHERE "Id" = historical."Id";
                            END IF;
                        EXCEPTION WHEN invalid_text_representation THEN
                            -- Do not invent a result from unstructured old text.
                            NULL;
                        END;
                    END LOOP;
                END $audit_outcomes$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An older binary cannot represent arbitrary custom/multiple roles.
            // Preserve an explicit restriction so downgrade can only deny grants,
            // never reactivate a removed built-in default. Restore a pre-upgrade
            // backup when exact custom-role rollback is required.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" u SET "AccessConfigured" = true,
                    "AccessPermissions" = ARRAY(SELECT DISTINCT p."Permission" FROM "AspNetUserRoles" m JOIN "RolePermissions" p ON p."RoleId" = m."RoleId" WHERE m."UserId" = u."Id" AND p."Permission" <> 'platform.manage'),
                    "SecurityStamp" = md5(random()::text || u."Id")
                WHERE NOT EXISTS (SELECT 1 FROM "AspNetUserRoles" m JOIN "AspNetRoles" r ON r."Id" = m."RoleId" WHERE m."UserId" = u."Id" AND r."Name" = 'Admin');
                """);
            migrationBuilder.DropTable(
                name: "RoleDefinitions");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Category_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorName",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ApplicationName",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SourceIp",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetName",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "AuditLogs");
        }
    }
}
