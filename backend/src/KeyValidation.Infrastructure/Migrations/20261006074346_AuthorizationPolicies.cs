using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuthorizationPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActivationValidityDays",
                table: "Plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Features",
                table: "Plans",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrentSessions",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "KeyDigest",
                table: "Licenses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "Licenses",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivateBefore",
                table: "Licenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Features",
                table: "Licenses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FrozenAt",
                table: "Licenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrial",
                table: "Licenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrentSessions",
                table: "Licenses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Preserve the concurrency behavior of every previously issued card
            // and plan before validating the new independent session limit.
            migrationBuilder.Sql("UPDATE \"Plans\" SET \"MaxConcurrentSessions\" = \"MaxDevices\"");
            migrationBuilder.Sql("UPDATE \"Licenses\" SET \"MaxConcurrentSessions\" = \"MaxDevices\"");

            migrationBuilder.CreateTable(
                name: "LicenseFreezes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrozenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnfrozenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemainingSeconds = table.Column<double>(type: "double precision", nullable: false),
                    FrozenBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UnfrozenBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    FreezeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UnfreezeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseFreezes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseFreezes_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrialClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpgradedToLicenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpgradedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialClaims_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrialClaims_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrialClaims_Licenses_UpgradedToLicenseId",
                        column: x => x.UpgradedToLicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrialPolicies",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Features = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialPolicies", x => x.ApplicationId);
                    table.CheckConstraint("CK_TrialPolicies_Duration", "\"DurationSeconds\" BETWEEN 1 AND 2592000");
                    table.ForeignKey(
                        name: "FK_TrialPolicies_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_ActivationDays",
                table: "Plans",
                sql: "\"ActivationValidityDays\" IS NULL OR \"ActivationValidityDays\" BETWEEN 1 AND 3650");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Sessions",
                table: "Plans",
                sql: "\"MaxConcurrentSessions\" BETWEEN 1 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ActivateBefore",
                table: "Licenses",
                column: "ActivateBefore");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Licenses_Sessions",
                table: "Licenses",
                sql: "\"MaxConcurrentSessions\" BETWEEN 1 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFreezes_LicenseId",
                table: "LicenseFreezes",
                column: "LicenseId",
                unique: true,
                filter: "\"UnfrozenAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFreezes_LicenseId_FrozenAt",
                table: "LicenseFreezes",
                columns: new[] { "LicenseId", "FrozenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrialClaims_ApplicationId_DeviceDigest",
                table: "TrialClaims",
                columns: new[] { "ApplicationId", "DeviceDigest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialClaims_LicenseId",
                table: "TrialClaims",
                column: "LicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialClaims_UpgradedToLicenseId",
                table: "TrialClaims",
                column: "UpgradedToLicenseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseFreezes");

            migrationBuilder.DropTable(
                name: "TrialClaims");

            migrationBuilder.DropTable(
                name: "TrialPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_ActivationDays",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Sessions",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_ActivateBefore",
                table: "Licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Licenses_Sessions",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ActivationValidityDays",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Features",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxConcurrentSessions",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ActivateBefore",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Features",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "IsTrial",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "MaxConcurrentSessions",
                table: "Licenses");

            migrationBuilder.AlterColumn<string>(
                name: "KeyDigest",
                table: "Licenses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "Licenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
