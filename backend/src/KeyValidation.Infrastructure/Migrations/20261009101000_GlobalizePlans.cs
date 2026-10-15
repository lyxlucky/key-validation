using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalizePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Plans_Applications_ApplicationId",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Plans_ApplicationId_CreatedAt",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "Plans");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_CreatedAt_Id",
                table: "Plans",
                columns: new[] { "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
    DO $$
    BEGIN
        IF EXISTS (
            SELECT p."Id"
            FROM "Plans" p LEFT JOIN "ApplicationPlans" r ON r."PlanId" = p."Id"
            GROUP BY p."Id" HAVING COUNT(r."PlanId") <> 1
        ) THEN
            RAISE EXCEPTION 'SHARED_PLAN_DOWNGRADE_UNSUPPORTED';
        END IF;
    END $$;
    """);
            migrationBuilder.DropIndex(
                name: "IX_Plans_CreatedAt_Id",
                table: "Plans");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId", table: "Plans", type: "uuid", nullable: true);
            migrationBuilder.Sql("""
    UPDATE "Plans" p
    SET "ApplicationId" = r."ApplicationId",
        "IsEnabled" = p."IsEnabled" AND r."IsEnabled"
    FROM "ApplicationPlans" r WHERE r."PlanId" = p."Id";
    """);
            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationId", table: "Plans", type: "uuid",
                nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_ApplicationId_CreatedAt",
                table: "Plans",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Applications_ApplicationId",
                table: "Plans",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
