using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManagementExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Licenses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "Licenses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisabledAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ExpiresAt",
                table: "Licenses",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OperationReceipts_ActorId_RequestId",
                table: "OperationReceipts",
                columns: new[] { "ActorId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_ExpiresAt",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                table: "AspNetUsers");
        }
    }
}
