using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountSecurityRebinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RebindingCooldownHours",
                table: "Licenses",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<bool>(
                name: "RebindingEnabled",
                table: "Licenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "RebindingGeneration",
                table: "Licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "RebindingMaxChanges",
                table: "Licenses",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "RebindingWindowDays",
                table: "Licenses",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<long>(
                name: "LastTotpStep",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LoginChallengeId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingTotpExpiresAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingTotpSecurityStamp",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPendingTotpSecret",
                table: "AspNetUsers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedTotpSecret",
                table: "AspNetUsers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminRecoveryCodes",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRecoveryCodes", x => new { x.UserId, x.Digest });
                    table.ForeignKey(
                        name: "FK_AdminRecoveryCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RebindingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebindingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebindingEvents_Bindings_BindingId",
                        column: x => x.BindingId,
                        principalTable: "Bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebindingEvents_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Licenses_RebindingCooldownHours",
                table: "Licenses",
                sql: "\"RebindingCooldownHours\" BETWEEN 0 AND 8760");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Licenses_RebindingGeneration",
                table: "Licenses",
                sql: "\"RebindingGeneration\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Licenses_RebindingMaxChanges",
                table: "Licenses",
                sql: "\"RebindingMaxChanges\" BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Licenses_RebindingWindowDays",
                table: "Licenses",
                sql: "\"RebindingWindowDays\" BETWEEN 1 AND 365");

            migrationBuilder.CreateIndex(
                name: "IX_RebindingEvents_BindingId",
                table: "RebindingEvents",
                column: "BindingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RebindingEvents_LicenseId_Generation_ChangedAt",
                table: "RebindingEvents",
                columns: new[] { "LicenseId", "Generation", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminRecoveryCodes");

            migrationBuilder.DropTable(
                name: "RebindingEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Licenses_RebindingCooldownHours",
                table: "Licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Licenses_RebindingGeneration",
                table: "Licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Licenses_RebindingMaxChanges",
                table: "Licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Licenses_RebindingWindowDays",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RebindingCooldownHours",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RebindingEnabled",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RebindingGeneration",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RebindingMaxChanges",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RebindingWindowDays",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "LastTotpStep",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LoginChallengeId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PendingTotpExpiresAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PendingTotpSecurityStamp",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProtectedPendingTotpSecret",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProtectedTotpSecret",
                table: "AspNetUsers");
        }
    }
}
