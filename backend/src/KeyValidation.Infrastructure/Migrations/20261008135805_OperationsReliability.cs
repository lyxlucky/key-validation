using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationsReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrustedCountryCode",
                table: "AuthorizationEvents",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationDurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    VerificationErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SampledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DatabaseAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    ServerErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    ErrorRate = table.Column<double>(type: "double precision", nullable: true),
                    P95LatencyMs = table.Column<double>(type: "double precision", nullable: true),
                    RoutesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPolicies",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecipientIds = table.Column<string[]>(type: "text[]", nullable: false),
                    EventTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    ExpiryDays = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicies", x => x.ApplicationId);
                    table.ForeignKey(
                        name: "FK_NotificationPolicies_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BodyJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskPolicies",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActivationThreshold = table.Column<int>(type: "integer", nullable: false),
                    ActivationWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    RebindingThreshold = table.Column<int>(type: "integer", nullable: false),
                    RebindingWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    RegionWindowHours = table.Column<int>(type: "integer", nullable: false),
                    ScannedThrough = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskPolicies", x => x.ApplicationId);
                    table.ForeignKey(
                        name: "FK_RiskPolicies_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffNotifications_OperationsEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "OperationsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_OperationsEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "OperationsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_WebhookSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationEvents_ApplicationId_LicenseId_CreatedAt",
                table: "AuthorizationEvents",
                columns: new[] { "ApplicationId", "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_CreatedAt",
                table: "BackupJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringIncidents_Kind",
                table: "MonitoringIncidents",
                column: "Kind",
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringIncidents_StartedAt",
                table: "MonitoringIncidents",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSamples_SampledAt",
                table: "MonitoringSamples",
                column: "SampledAt");

            migrationBuilder.CreateIndex(
                name: "IX_OperationsEvents_ApplicationId_DeduplicationKey",
                table: "OperationsEvents",
                columns: new[] { "ApplicationId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationsEvents_DispatchedAt_CreatedAt",
                table: "OperationsEvents",
                columns: new[] { "DispatchedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvents_ApplicationId_CreatedAt",
                table: "RiskEvents",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvents_LicenseId_Rule_WindowStart",
                table: "RiskEvents",
                columns: new[] { "LicenseId", "Rule", "WindowStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffNotifications_EventId_RecipientId",
                table: "StaffNotifications",
                columns: new[] { "EventId", "RecipientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffNotifications_RecipientId_CreatedAt",
                table: "StaffNotifications",
                columns: new[] { "RecipientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EventId_SubscriptionId",
                table: "WebhookDeliveries",
                columns: new[] { "EventId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Status_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SubscriptionId",
                table: "WebhookDeliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_ApplicationId",
                table: "WebhookSubscriptions",
                column: "ApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropTable(
                name: "MonitoringIncidents");

            migrationBuilder.DropTable(
                name: "MonitoringSamples");

            migrationBuilder.DropTable(
                name: "NotificationPolicies");

            migrationBuilder.DropTable(
                name: "RiskEvents");

            migrationBuilder.DropTable(
                name: "RiskPolicies");

            migrationBuilder.DropTable(
                name: "StaffNotifications");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "OperationsEvents");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AuthorizationEvents_ApplicationId_LicenseId_CreatedAt",
                table: "AuthorizationEvents");

            migrationBuilder.DropColumn(
                name: "TrustedCountryCode",
                table: "AuthorizationEvents");
        }
    }
}
