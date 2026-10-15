using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClientExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientVersion",
                table: "Sessions",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationVersionPolicies",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumVersion = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    LatestVersion = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    EnforceMinimum = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReleaseNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationVersionPolicies", x => x.ApplicationId);
                    table.ForeignKey(
                        name: "FK_ApplicationVersionPolicies_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfflineLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Features = table.Column<string[]>(type: "text[]", nullable: false),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineLicenses", x => x.Id);
                    table.CheckConstraint("CK_OfflineLicenses_Times", "\"IssuedAt\" <= \"NotBefore\" AND \"NotBefore\" < \"ExpiresAt\" AND \"ExpiresAt\" <= \"IssuedAt\" + INTERVAL '168 hours'");
                    table.ForeignKey(
                        name: "FK_OfflineLicenses_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfflineLicenses_Bindings_BindingId",
                        column: x => x.BindingId,
                        principalTable: "Bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfflineLicenses_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLicenses_ApplicationId_IssuedAt",
                table: "OfflineLicenses",
                columns: new[] { "ApplicationId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLicenses_BindingId",
                table: "OfflineLicenses",
                column: "BindingId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLicenses_LicenseId_IssuedAt",
                table: "OfflineLicenses",
                columns: new[] { "LicenseId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLicenses_Serial",
                table: "OfflineLicenses",
                column: "Serial",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationVersionPolicies");

            migrationBuilder.DropTable(
                name: "OfflineLicenses");

            migrationBuilder.DropColumn(
                name: "ClientVersion",
                table: "Sessions");
        }
    }
}
