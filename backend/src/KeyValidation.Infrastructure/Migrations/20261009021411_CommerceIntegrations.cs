using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommerceIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_AspNetUsers_CreatedBy",
                table: "Batches");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "StaffNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "OperationsEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "Licenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Licenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Batches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "Batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ApplicationIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    RemainingQuota = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.CheckConstraint("CK_Channels_Quota", "\"RemainingQuota\" >= 0");
                    table.ForeignKey(
                        name: "FK_Channels_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    SecretDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiCredentials_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApiCredentials_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelPrices",
                columns: table => new
                {
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPriceCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelPrices", x => new { x.ChannelId, x.PlanId });
                    table.CheckConstraint("CK_ChannelPrices_Price", "\"UnitPriceCents\" >= 0");
                    table.ForeignKey(
                        name: "FK_ChannelPrices_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelPrices_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnonymizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessCodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccessVersion = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceCents = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RenewalLicenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: false),
                    MaxDevices = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrentSessions = table.Column<int>(type: "integer", nullable: false),
                    ActivationValidityDays = table.Column<int>(type: "integer", nullable: true),
                    Features = table.Column<string[]>(type: "text[]", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryCiphertext = table.Column<string>(type: "text", nullable: true),
                    DeliveryExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Price", "\"UnitPriceCents\" >= 0");
                    table.CheckConstraint("CK_Orders_Quantity", "\"Quantity\" BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_Orders_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Licenses_RenewalLicenseId",
                        column: x => x.RenewalLicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortalSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccessVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalSessions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelQuotaEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Delta = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelQuotaEntries", x => x.Id);
                    table.CheckConstraint("CK_ChannelQuotaEntries_Balance", "\"BalanceAfter\" >= 0");
                    table.ForeignKey(
                        name: "FK_ChannelQuotaEntries_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelQuotaEntries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderHistories_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderLicenses",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLicenses", x => new { x.OrderId, x.LicenseId });
                    table.ForeignKey(
                        name: "FK_OrderLicenses_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLicenses_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalRequests_Bindings_BindingId",
                        column: x => x.BindingId,
                        principalTable: "Bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortalRequests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortalRequests_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortalRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ApplicationId_ChannelId_CreatedAt",
                table: "Licenses",
                columns: new[] { "ApplicationId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ChannelId",
                table: "Licenses",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_CustomerId",
                table: "Licenses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ApplicationId_ChannelId_CreatedAt",
                table: "Batches",
                columns: new[] { "ApplicationId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ChannelId",
                table: "Batches",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_CustomerId",
                table: "Batches",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_ApplicationId",
                table: "ApiCredentials",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_ChannelId",
                table: "ApiCredentials",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_SecretDigest",
                table: "ApiCredentials",
                column: "SecretDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPrices_PlanId",
                table: "ChannelPrices",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelQuotaEntries_ChannelId_Kind_RequestId",
                table: "ChannelQuotaEntries",
                columns: new[] { "ChannelId", "Kind", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelQuotaEntries_OrderId",
                table: "ChannelQuotaEntries",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_AccountId",
                table: "Channels",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceReceipts_ActorId_Operation_RequestId",
                table: "CommerceReceipts",
                columns: new[] { "ActorId", "Operation", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AccessCodeDigest",
                table: "Customers",
                column: "AccessCodeDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ApplicationId_ChannelId_CreatedAt",
                table: "Customers",
                columns: new[] { "ApplicationId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ChannelId",
                table: "Customers",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHistories_OrderId_CreatedAt",
                table: "OrderHistories",
                columns: new[] { "OrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLicenses_LicenseId",
                table: "OrderLicenses",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ApplicationId_ChannelId_CreatedAt",
                table: "Orders",
                columns: new[] { "ApplicationId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BatchId",
                table: "Orders",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ChannelId",
                table: "Orders",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedBy_RequestId",
                table: "Orders",
                columns: new[] { "CreatedBy", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DeliveryExpiresAt",
                table: "Orders",
                column: "DeliveryExpiresAt",
                filter: "\"DeliveryCiphertext\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PlanId",
                table: "Orders",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RenewalLicenseId",
                table: "Orders",
                column: "RenewalLicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalRequests_ApplicationId_ChannelId_CreatedAt",
                table: "PortalRequests",
                columns: new[] { "ApplicationId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalRequests_BindingId",
                table: "PortalRequests",
                column: "BindingId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalRequests_CustomerId_RequestId",
                table: "PortalRequests",
                columns: new[] { "CustomerId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalRequests_LicenseId",
                table: "PortalRequests",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalRequests_OrderId",
                table: "PortalRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalSessions_CustomerId_ExpiresAt",
                table: "PortalSessions",
                columns: new[] { "CustomerId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalSessions_TokenDigest",
                table: "PortalSessions",
                column: "TokenDigest",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_Channels_ChannelId",
                table: "Batches",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_Customers_CustomerId",
                table: "Batches",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Channels_ChannelId",
                table: "Licenses",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Customers_CustomerId",
                table: "Licenses",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION keyvalidation_immutable_quota_ledger() RETURNS trigger LANGUAGE plpgsql AS $ledger$
                BEGIN RAISE EXCEPTION 'QUOTA_LEDGER_IMMUTABLE'; END $ledger$;
                CREATE TRIGGER immutable_quota_ledger BEFORE UPDATE OR DELETE ON "ChannelQuotaEntries"
                FOR EACH ROW EXECUTE FUNCTION keyvalidation_immutable_quota_ledger();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Older binaries cannot represent commerce ownership or API actors.
            // Refuse a destructive downgrade once these facts exist.
            migrationBuilder.Sql("""
                DO $commerce$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Customers") OR EXISTS (SELECT 1 FROM "Channels") OR EXISTS (SELECT 1 FROM "ApiCredentials") THEN
                        RAISE EXCEPTION 'COMMERCE_DOWNGRADE_REQUIRES_BACKUP_RESTORE';
                    END IF;
                END $commerce$;
                DROP TRIGGER immutable_quota_ledger ON "ChannelQuotaEntries";
                DROP FUNCTION keyvalidation_immutable_quota_ledger();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Channels_ChannelId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Customers_CustomerId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Channels_ChannelId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Customers_CustomerId",
                table: "Licenses");

            migrationBuilder.DropTable(
                name: "ApiCredentials");

            migrationBuilder.DropTable(
                name: "ChannelPrices");

            migrationBuilder.DropTable(
                name: "ChannelQuotaEntries");

            migrationBuilder.DropTable(
                name: "CommerceReceipts");

            migrationBuilder.DropTable(
                name: "OrderHistories");

            migrationBuilder.DropTable(
                name: "OrderLicenses");

            migrationBuilder.DropTable(
                name: "PortalRequests");

            migrationBuilder.DropTable(
                name: "PortalSessions");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_ApplicationId_ChannelId_CreatedAt",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_ChannelId",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_CustomerId",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ApplicationId_ChannelId_CreatedAt",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ChannelId",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_CustomerId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "OperationsEvents");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Batches",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_AspNetUsers_CreatedBy",
                table: "Batches",
                column: "CreatedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
