using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationsAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AccessAllApplications",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "AccessApplicationIds",
                table: "AspNetUsers",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "AccessChannelId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AccessConfigured",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "AccessPermissions",
                table: "AspNetUsers",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            // Recover ownership from durable targets. Global/account events and
            // targets already removed by retention stay null and are hidden from scoped accounts.
            migrationBuilder.Sql("""
                UPDATE "AuditLogs" a SET "ApplicationId" = app."Id"
                FROM "Applications" app WHERE a."TargetId" = app."Id" AND a."Action" LIKE 'application.%';
                UPDATE "AuditLogs" a SET "ApplicationId" = p."ApplicationId"
                FROM "Plans" p WHERE a."TargetId" = p."Id" AND a."Action" LIKE 'plan.%';
                UPDATE "AuditLogs" a SET "ApplicationId" = b."ApplicationId"
                FROM "Batches" b WHERE a."TargetId" = b."Id" AND a."Action" LIKE 'batch.%';
                UPDATE "AuditLogs" a SET "ApplicationId" = l."ApplicationId"
                FROM "Licenses" l WHERE a."TargetId" = l."Id" AND a."Action" LIKE 'license.%';
                UPDATE "AuditLogs" a SET "ApplicationId" = l."ApplicationId"
                FROM "Bindings" b JOIN "Licenses" l ON b."LicenseId" = l."Id"
                WHERE a."TargetId" = b."Id" AND a."Action" LIKE 'binding.%';
                UPDATE "AuditLogs" a SET "ApplicationId" = l."ApplicationId"
                FROM "Sessions" s JOIN "Bindings" b ON s."BindingId" = b."Id" JOIN "Licenses" l ON b."LicenseId" = l."Id"
                WHERE a."TargetId" = s."Id" AND a."Action" LIKE 'session.%';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ApplicationId_ActivatedAt",
                table: "Licenses",
                columns: new[] { "ApplicationId", "ActivatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bindings_DeviceDigest_LicenseId",
                table: "Bindings",
                columns: new[] { "DeviceDigest", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ApplicationId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "ApplicationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Licenses_ApplicationId_ActivatedAt",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Bindings_DeviceDigest_LicenseId",
                table: "Bindings");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ApplicationId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "AccessAllApplications",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccessApplicationIds",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccessChannelId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccessConfigured",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccessPermissions",
                table: "AspNetUsers");
        }
    }
}
