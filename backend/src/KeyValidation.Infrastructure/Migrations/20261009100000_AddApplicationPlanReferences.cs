using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationPlanReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices",
                columns: new[] { "ChannelId", "ApplicationId", "PlanId" });

            migrationBuilder.CreateTable(
                name: "ApplicationPlans",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationPlans", x => new { x.ApplicationId, x.PlanId });
                    table.ForeignKey(
                        name: "FK_ApplicationPlans_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationPlans_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ApplicationId_PlanId",
                table: "Orders",
                columns: new[] { "ApplicationId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPrices_ApplicationId_PlanId",
                table: "ChannelPrices",
                columns: new[] { "ApplicationId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ApplicationId_PlanId",
                table: "Batches",
                columns: new[] { "ApplicationId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationPlans_PlanId",
                table: "ApplicationPlans",
                column: "PlanId");

            migrationBuilder.Sql("""
                INSERT INTO "ApplicationPlans" ("ApplicationId", "PlanId", "IsEnabled", "CreatedAt")
                SELECT "ApplicationId", "Id", TRUE, "CreatedAt" FROM "Plans";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_ApplicationPlans_ApplicationId_PlanId",
                table: "Batches",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelPrices_ApplicationPlans_ApplicationId_PlanId",
                table: "ChannelPrices",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ApplicationPlans_ApplicationId_PlanId",
                table: "Orders",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_ApplicationPlans_ApplicationId_PlanId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelPrices_ApplicationPlans_ApplicationId_PlanId",
                table: "ChannelPrices");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ApplicationPlans_ApplicationId_PlanId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "ApplicationPlans");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ApplicationId_PlanId",
                table: "Orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices");

            migrationBuilder.DropIndex(
                name: "IX_ChannelPrices_ApplicationId_PlanId",
                table: "ChannelPrices");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ApplicationId_PlanId",
                table: "Batches");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices",
                columns: new[] { "ChannelId", "PlanId" });
        }
    }
}
