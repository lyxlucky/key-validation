using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KeyValidation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrefixTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdminRecoveryCodes_AspNetUsers_UserId",
                table: "AdminRecoveryCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiCredentials_Applications_ApplicationId",
                table: "ApiCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiCredentials_Channels_ChannelId",
                table: "ApiCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationPlans_Applications_ApplicationId",
                table: "ApplicationPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationPlans_Plans_PlanId",
                table: "ApplicationPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationVersionPolicies_Applications_ApplicationId",
                table: "ApplicationVersionPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                table: "AspNetRoleClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                table: "AspNetUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_ApplicationPlans_ApplicationId_PlanId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Applications_ApplicationId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Channels_ChannelId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Customers_CustomerId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Plans_PlanId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_Bindings_Licenses_LicenseId",
                table: "Bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelPrices_ApplicationPlans_ApplicationId_PlanId",
                table: "ChannelPrices");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelPrices_Channels_ChannelId",
                table: "ChannelPrices");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelPrices_Plans_PlanId",
                table: "ChannelPrices");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelQuotaEntries_Channels_ChannelId",
                table: "ChannelQuotaEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelQuotaEntries_Orders_OrderId",
                table: "ChannelQuotaEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_AspNetUsers_AccountId",
                table: "Channels");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Applications_ApplicationId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Channels_ChannelId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_LicenseFreezes_Licenses_LicenseId",
                table: "LicenseFreezes");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Applications_ApplicationId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Batches_BatchId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Channels_ChannelId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Customers_CustomerId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationPolicies_Applications_ApplicationId",
                table: "NotificationPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_OfflineLicenses_Applications_ApplicationId",
                table: "OfflineLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_OfflineLicenses_Bindings_BindingId",
                table: "OfflineLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_OfflineLicenses_Licenses_LicenseId",
                table: "OfflineLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderHistories_Orders_OrderId",
                table: "OrderHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLicenses_Licenses_LicenseId",
                table: "OrderLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLicenses_Orders_OrderId",
                table: "OrderLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ApplicationPlans_ApplicationId_PlanId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Applications_ApplicationId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Batches_BatchId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Channels_ChannelId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Licenses_RenewalLicenseId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Plans_PlanId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_PortalRequests_Bindings_BindingId",
                table: "PortalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PortalRequests_Customers_CustomerId",
                table: "PortalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PortalRequests_Licenses_LicenseId",
                table: "PortalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PortalRequests_Orders_OrderId",
                table: "PortalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PortalSessions_Customers_CustomerId",
                table: "PortalSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_RebindingEvents_Bindings_BindingId",
                table: "RebindingEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_RebindingEvents_Licenses_LicenseId",
                table: "RebindingEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskPolicies_Applications_ApplicationId",
                table: "RiskPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleDefinitions_AspNetRoles_RoleId",
                table: "RoleDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleDefinitions_AspNetUsers_LegacyUserId",
                table: "RoleDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_AspNetRoles_RoleId",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Bindings_BindingId",
                table: "Sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffNotifications_OperationsEvents_EventId",
                table: "StaffNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TrialClaims_Applications_ApplicationId",
                table: "TrialClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_TrialClaims_Licenses_LicenseId",
                table: "TrialClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_TrialClaims_Licenses_UpgradedToLicenseId",
                table: "TrialClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_TrialPolicies_Applications_ApplicationId",
                table: "TrialPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_OperationsEvents_EventId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_SubscriptionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookSubscriptions_Applications_ApplicationId",
                table: "WebhookSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeliveries",
                table: "WebhookDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TrialPolicies",
                table: "TrialPolicies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TrialClaims",
                table: "TrialClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StaffNotifications",
                table: "StaffNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RolePermissions",
                table: "RolePermissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RoleDefinitions",
                table: "RoleDefinitions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RiskPolicies",
                table: "RiskPolicies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RiskEvents",
                table: "RiskEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RebindingEvents",
                table: "RebindingEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PortalSessions",
                table: "PortalSessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PortalRequests",
                table: "PortalRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Plans",
                table: "Plans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Orders",
                table: "Orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderLicenses",
                table: "OrderLicenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderHistories",
                table: "OrderHistories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OperationsEvents",
                table: "OperationsEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OperationReceipts",
                table: "OperationReceipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OfflineLicenses",
                table: "OfflineLicenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationPolicies",
                table: "NotificationPolicies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MonitoringSamples",
                table: "MonitoringSamples");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MonitoringIncidents",
                table: "MonitoringIncidents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LicenseFreezes",
                table: "LicenseFreezes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customers",
                table: "Customers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CommerceReceipts",
                table: "CommerceReceipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Channels",
                table: "Channels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ChannelQuotaEntries",
                table: "ChannelQuotaEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Bindings",
                table: "Bindings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Batches",
                table: "Batches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BackupJobs",
                table: "BackupJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuthorizationEvents",
                table: "AuthorizationEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserTokens",
                table: "AspNetUserTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserRoles",
                table: "AspNetUserRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserLogins",
                table: "AspNetUserLogins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserClaims",
                table: "AspNetUserClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetRoles",
                table: "AspNetRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetRoleClaims",
                table: "AspNetRoleClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApplicationVersionPolicies",
                table: "ApplicationVersionPolicies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Applications",
                table: "Applications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApplicationPlans",
                table: "ApplicationPlans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApiCredentials",
                table: "ApiCredentials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AdminRecoveryCodes",
                table: "AdminRecoveryCodes");

            migrationBuilder.RenameTable(
                name: "WebhookSubscriptions",
                newName: "t_webhook_subscriptions");

            migrationBuilder.RenameTable(
                name: "WebhookDeliveries",
                newName: "t_webhook_deliveries");

            migrationBuilder.RenameTable(
                name: "TrialPolicies",
                newName: "t_trial_policies");

            migrationBuilder.RenameTable(
                name: "TrialClaims",
                newName: "t_trial_claims");

            migrationBuilder.RenameTable(
                name: "StaffNotifications",
                newName: "t_staff_notifications");

            migrationBuilder.RenameTable(
                name: "Sessions",
                newName: "t_sessions");

            migrationBuilder.RenameTable(
                name: "RolePermissions",
                newName: "t_role_permissions");

            migrationBuilder.RenameTable(
                name: "RoleDefinitions",
                newName: "t_role_definitions");

            migrationBuilder.RenameTable(
                name: "RiskPolicies",
                newName: "t_risk_policies");

            migrationBuilder.RenameTable(
                name: "RiskEvents",
                newName: "t_risk_events");

            migrationBuilder.RenameTable(
                name: "RebindingEvents",
                newName: "t_rebinding_events");

            migrationBuilder.RenameTable(
                name: "PortalSessions",
                newName: "t_portal_sessions");

            migrationBuilder.RenameTable(
                name: "PortalRequests",
                newName: "t_portal_requests");

            migrationBuilder.RenameTable(
                name: "Plans",
                newName: "t_plans");

            migrationBuilder.RenameTable(
                name: "Orders",
                newName: "t_orders");

            migrationBuilder.RenameTable(
                name: "OrderLicenses",
                newName: "t_order_licenses");

            migrationBuilder.RenameTable(
                name: "OrderHistories",
                newName: "t_order_histories");

            migrationBuilder.RenameTable(
                name: "OperationsEvents",
                newName: "t_operations_events");

            migrationBuilder.RenameTable(
                name: "OperationReceipts",
                newName: "t_operation_receipts");

            migrationBuilder.RenameTable(
                name: "OfflineLicenses",
                newName: "t_offline_licenses");

            migrationBuilder.RenameTable(
                name: "NotificationPolicies",
                newName: "t_notification_policies");

            migrationBuilder.RenameTable(
                name: "MonitoringSamples",
                newName: "t_monitoring_samples");

            migrationBuilder.RenameTable(
                name: "MonitoringIncidents",
                newName: "t_monitoring_incidents");

            migrationBuilder.RenameTable(
                name: "Licenses",
                newName: "t_licenses");

            migrationBuilder.RenameTable(
                name: "LicenseFreezes",
                newName: "t_license_freezes");

            migrationBuilder.RenameTable(
                name: "Customers",
                newName: "t_customers");

            migrationBuilder.RenameTable(
                name: "CommerceReceipts",
                newName: "t_commerce_receipts");

            migrationBuilder.RenameTable(
                name: "Channels",
                newName: "t_channels");

            migrationBuilder.RenameTable(
                name: "ChannelQuotaEntries",
                newName: "t_channel_quota_entries");

            migrationBuilder.RenameTable(
                name: "ChannelPrices",
                newName: "t_channel_prices");

            migrationBuilder.RenameTable(
                name: "Bindings",
                newName: "t_bindings");

            migrationBuilder.RenameTable(
                name: "Batches",
                newName: "t_batches");

            migrationBuilder.RenameTable(
                name: "BackupJobs",
                newName: "t_backup_jobs");

            migrationBuilder.RenameTable(
                name: "AuthorizationEvents",
                newName: "t_authorization_events");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "t_audit_logs");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "t_asp_net_user_tokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "t_asp_net_users");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                newName: "t_asp_net_user_roles");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "t_asp_net_user_logins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "t_asp_net_user_claims");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                newName: "t_asp_net_roles");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                newName: "t_asp_net_role_claims");

            migrationBuilder.RenameTable(
                name: "ApplicationVersionPolicies",
                newName: "t_application_version_policies");

            migrationBuilder.RenameTable(
                name: "Applications",
                newName: "t_applications");

            migrationBuilder.RenameTable(
                name: "ApplicationPlans",
                newName: "t_application_plans");

            migrationBuilder.RenameTable(
                name: "ApiCredentials",
                newName: "t_api_credentials");

            migrationBuilder.RenameTable(
                name: "AdminRecoveryCodes",
                newName: "t_admin_recovery_codes");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookSubscriptions_ApplicationId",
                table: "t_webhook_subscriptions",
                newName: "IX_t_webhook_subscriptions_ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_SubscriptionId",
                table: "t_webhook_deliveries",
                newName: "IX_t_webhook_deliveries_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_Status_NextAttemptAt",
                table: "t_webhook_deliveries",
                newName: "IX_t_webhook_deliveries_Status_NextAttemptAt");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_EventId_SubscriptionId",
                table: "t_webhook_deliveries",
                newName: "IX_t_webhook_deliveries_EventId_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_TrialClaims_UpgradedToLicenseId",
                table: "t_trial_claims",
                newName: "IX_t_trial_claims_UpgradedToLicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_TrialClaims_LicenseId",
                table: "t_trial_claims",
                newName: "IX_t_trial_claims_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_TrialClaims_ApplicationId_DeviceDigest",
                table: "t_trial_claims",
                newName: "IX_t_trial_claims_ApplicationId_DeviceDigest");

            migrationBuilder.RenameIndex(
                name: "IX_StaffNotifications_RecipientId_CreatedAt",
                table: "t_staff_notifications",
                newName: "IX_t_staff_notifications_RecipientId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_StaffNotifications_EventId_RecipientId",
                table: "t_staff_notifications",
                newName: "IX_t_staff_notifications_EventId_RecipientId");

            migrationBuilder.RenameIndex(
                name: "IX_Sessions_TokenDigest",
                table: "t_sessions",
                newName: "IX_t_sessions_TokenDigest");

            migrationBuilder.RenameIndex(
                name: "IX_Sessions_LeaseExpiresAt",
                table: "t_sessions",
                newName: "IX_t_sessions_LeaseExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_Sessions_BindingId",
                table: "t_sessions",
                newName: "IX_t_sessions_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_RolePermissions_Permission_RoleId",
                table: "t_role_permissions",
                newName: "IX_t_role_permissions_Permission_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_RoleDefinitions_LegacyUserId",
                table: "t_role_definitions",
                newName: "IX_t_role_definitions_LegacyUserId");

            migrationBuilder.RenameIndex(
                name: "IX_RiskEvents_LicenseId_Rule_WindowStart",
                table: "t_risk_events",
                newName: "IX_t_risk_events_LicenseId_Rule_WindowStart");

            migrationBuilder.RenameIndex(
                name: "IX_RiskEvents_ApplicationId_CreatedAt",
                table: "t_risk_events",
                newName: "IX_t_risk_events_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_RebindingEvents_LicenseId_Generation_ChangedAt",
                table: "t_rebinding_events",
                newName: "IX_t_rebinding_events_LicenseId_Generation_ChangedAt");

            migrationBuilder.RenameIndex(
                name: "IX_RebindingEvents_BindingId",
                table: "t_rebinding_events",
                newName: "IX_t_rebinding_events_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_PortalSessions_TokenDigest",
                table: "t_portal_sessions",
                newName: "IX_t_portal_sessions_TokenDigest");

            migrationBuilder.RenameIndex(
                name: "IX_PortalSessions_CustomerId_ExpiresAt",
                table: "t_portal_sessions",
                newName: "IX_t_portal_sessions_CustomerId_ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_PortalRequests_OrderId",
                table: "t_portal_requests",
                newName: "IX_t_portal_requests_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_PortalRequests_LicenseId",
                table: "t_portal_requests",
                newName: "IX_t_portal_requests_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_PortalRequests_CustomerId_RequestId",
                table: "t_portal_requests",
                newName: "IX_t_portal_requests_CustomerId_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_PortalRequests_BindingId",
                table: "t_portal_requests",
                newName: "IX_t_portal_requests_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_PortalRequests_ApplicationId_ChannelId_CreatedAt",
                table: "t_portal_requests",
                newName: "IX_t_portal_requests_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Plans_CreatedAt_Id",
                table: "t_plans",
                newName: "IX_t_plans_CreatedAt_Id");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_RenewalLicenseId",
                table: "t_orders",
                newName: "IX_t_orders_RenewalLicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_PlanId",
                table: "t_orders",
                newName: "IX_t_orders_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_DeliveryExpiresAt",
                table: "t_orders",
                newName: "IX_t_orders_DeliveryExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_CustomerId",
                table: "t_orders",
                newName: "IX_t_orders_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_CreatedBy_RequestId",
                table: "t_orders",
                newName: "IX_t_orders_CreatedBy_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_ChannelId",
                table: "t_orders",
                newName: "IX_t_orders_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_BatchId",
                table: "t_orders",
                newName: "IX_t_orders_BatchId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_ApplicationId_PlanId",
                table: "t_orders",
                newName: "IX_t_orders_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_ApplicationId_ChannelId_CreatedAt",
                table: "t_orders",
                newName: "IX_t_orders_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_OrderLicenses_LicenseId",
                table: "t_order_licenses",
                newName: "IX_t_order_licenses_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderHistories_OrderId_CreatedAt",
                table: "t_order_histories",
                newName: "IX_t_order_histories_OrderId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_OperationsEvents_DispatchedAt_CreatedAt",
                table: "t_operations_events",
                newName: "IX_t_operations_events_DispatchedAt_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_OperationsEvents_ApplicationId_DeduplicationKey",
                table: "t_operations_events",
                newName: "IX_t_operations_events_ApplicationId_DeduplicationKey");

            migrationBuilder.RenameIndex(
                name: "IX_OperationReceipts_ActorId_RequestId",
                table: "t_operation_receipts",
                newName: "IX_t_operation_receipts_ActorId_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_OfflineLicenses_Serial",
                table: "t_offline_licenses",
                newName: "IX_t_offline_licenses_Serial");

            migrationBuilder.RenameIndex(
                name: "IX_OfflineLicenses_LicenseId_IssuedAt",
                table: "t_offline_licenses",
                newName: "IX_t_offline_licenses_LicenseId_IssuedAt");

            migrationBuilder.RenameIndex(
                name: "IX_OfflineLicenses_BindingId",
                table: "t_offline_licenses",
                newName: "IX_t_offline_licenses_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_OfflineLicenses_ApplicationId_IssuedAt",
                table: "t_offline_licenses",
                newName: "IX_t_offline_licenses_ApplicationId_IssuedAt");

            migrationBuilder.RenameIndex(
                name: "IX_MonitoringSamples_SampledAt",
                table: "t_monitoring_samples",
                newName: "IX_t_monitoring_samples_SampledAt");

            migrationBuilder.RenameIndex(
                name: "IX_MonitoringIncidents_StartedAt",
                table: "t_monitoring_incidents",
                newName: "IX_t_monitoring_incidents_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_MonitoringIncidents_Kind",
                table: "t_monitoring_incidents",
                newName: "IX_t_monitoring_incidents_Kind");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_KeyDigest",
                table: "t_licenses",
                newName: "IX_t_licenses_KeyDigest");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ExpiresAt",
                table: "t_licenses",
                newName: "IX_t_licenses_ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_CustomerId",
                table: "t_licenses",
                newName: "IX_t_licenses_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ChannelId",
                table: "t_licenses",
                newName: "IX_t_licenses_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_BatchId",
                table: "t_licenses",
                newName: "IX_t_licenses_BatchId");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ApplicationId_CreatedAt",
                table: "t_licenses",
                newName: "IX_t_licenses_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ApplicationId_ChannelId_CreatedAt",
                table: "t_licenses",
                newName: "IX_t_licenses_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ApplicationId_ActivatedAt",
                table: "t_licenses",
                newName: "IX_t_licenses_ApplicationId_ActivatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Licenses_ActivateBefore",
                table: "t_licenses",
                newName: "IX_t_licenses_ActivateBefore");

            migrationBuilder.RenameIndex(
                name: "IX_LicenseFreezes_LicenseId_FrozenAt",
                table: "t_license_freezes",
                newName: "IX_t_license_freezes_LicenseId_FrozenAt");

            migrationBuilder.RenameIndex(
                name: "IX_LicenseFreezes_LicenseId",
                table: "t_license_freezes",
                newName: "IX_t_license_freezes_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_ChannelId",
                table: "t_customers",
                newName: "IX_t_customers_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_ApplicationId_ChannelId_CreatedAt",
                table: "t_customers",
                newName: "IX_t_customers_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_AccessCodeDigest",
                table: "t_customers",
                newName: "IX_t_customers_AccessCodeDigest");

            migrationBuilder.RenameIndex(
                name: "IX_CommerceReceipts_ActorId_Operation_RequestId",
                table: "t_commerce_receipts",
                newName: "IX_t_commerce_receipts_ActorId_Operation_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_Channels_AccountId",
                table: "t_channels",
                newName: "IX_t_channels_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_ChannelQuotaEntries_OrderId",
                table: "t_channel_quota_entries",
                newName: "IX_t_channel_quota_entries_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_ChannelQuotaEntries_ChannelId_Kind_RequestId",
                table: "t_channel_quota_entries",
                newName: "IX_t_channel_quota_entries_ChannelId_Kind_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_ChannelPrices_PlanId",
                table: "t_channel_prices",
                newName: "IX_t_channel_prices_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_ChannelPrices_ApplicationId_PlanId",
                table: "t_channel_prices",
                newName: "IX_t_channel_prices_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Bindings_LicenseId_DeviceDigest",
                table: "t_bindings",
                newName: "IX_t_bindings_LicenseId_DeviceDigest");

            migrationBuilder.RenameIndex(
                name: "IX_Bindings_DeviceDigest_LicenseId",
                table: "t_bindings",
                newName: "IX_t_bindings_DeviceDigest_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_PlanId",
                table: "t_batches",
                newName: "IX_t_batches_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_CustomerId",
                table: "t_batches",
                newName: "IX_t_batches_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_CreatedBy_RequestId",
                table: "t_batches",
                newName: "IX_t_batches_CreatedBy_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_ChannelId",
                table: "t_batches",
                newName: "IX_t_batches_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_ApplicationId_PlanId",
                table: "t_batches",
                newName: "IX_t_batches_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_ApplicationId_CreatedAt",
                table: "t_batches",
                newName: "IX_t_batches_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Batches_ApplicationId_ChannelId_CreatedAt",
                table: "t_batches",
                newName: "IX_t_batches_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_BackupJobs_CreatedAt",
                table: "t_backup_jobs",
                newName: "IX_t_backup_jobs_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuthorizationEvents_CreatedAt",
                table: "t_authorization_events",
                newName: "IX_t_authorization_events_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuthorizationEvents_ApplicationId_LicenseId_CreatedAt",
                table: "t_authorization_events",
                newName: "IX_t_authorization_events_ApplicationId_LicenseId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_TargetId_CreatedAt",
                table: "t_audit_logs",
                newName: "IX_t_audit_logs_TargetId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "t_audit_logs",
                newName: "IX_t_audit_logs_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_Category_CreatedAt",
                table: "t_audit_logs",
                newName: "IX_t_audit_logs_Category_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_ApplicationId_CreatedAt",
                table: "t_audit_logs",
                newName: "IX_t_audit_logs_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "t_asp_net_user_roles",
                newName: "IX_t_asp_net_user_roles_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "t_asp_net_user_logins",
                newName: "IX_t_asp_net_user_logins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "t_asp_net_user_claims",
                newName: "IX_t_asp_net_user_claims_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "t_asp_net_role_claims",
                newName: "IX_t_asp_net_role_claims_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_Applications_CreatedAt",
                table: "t_applications",
                newName: "IX_t_applications_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Applications_Code",
                table: "t_applications",
                newName: "IX_t_applications_Code");

            migrationBuilder.RenameIndex(
                name: "IX_ApplicationPlans_PlanId",
                table: "t_application_plans",
                newName: "IX_t_application_plans_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_ApiCredentials_SecretDigest",
                table: "t_api_credentials",
                newName: "IX_t_api_credentials_SecretDigest");

            migrationBuilder.RenameIndex(
                name: "IX_ApiCredentials_ChannelId",
                table: "t_api_credentials",
                newName: "IX_t_api_credentials_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_ApiCredentials_ApplicationId",
                table: "t_api_credentials",
                newName: "IX_t_api_credentials_ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_webhook_subscriptions",
                table: "t_webhook_subscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_webhook_deliveries",
                table: "t_webhook_deliveries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_trial_policies",
                table: "t_trial_policies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_trial_claims",
                table: "t_trial_claims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_staff_notifications",
                table: "t_staff_notifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_sessions",
                table: "t_sessions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_role_permissions",
                table: "t_role_permissions",
                columns: new[] { "RoleId", "Permission" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_role_definitions",
                table: "t_role_definitions",
                column: "RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_risk_policies",
                table: "t_risk_policies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_risk_events",
                table: "t_risk_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_rebinding_events",
                table: "t_rebinding_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_portal_sessions",
                table: "t_portal_sessions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_portal_requests",
                table: "t_portal_requests",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_plans",
                table: "t_plans",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_orders",
                table: "t_orders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_order_licenses",
                table: "t_order_licenses",
                columns: new[] { "OrderId", "LicenseId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_order_histories",
                table: "t_order_histories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_operations_events",
                table: "t_operations_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_operation_receipts",
                table: "t_operation_receipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_offline_licenses",
                table: "t_offline_licenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_notification_policies",
                table: "t_notification_policies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_monitoring_samples",
                table: "t_monitoring_samples",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_monitoring_incidents",
                table: "t_monitoring_incidents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_licenses",
                table: "t_licenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_license_freezes",
                table: "t_license_freezes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_customers",
                table: "t_customers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_commerce_receipts",
                table: "t_commerce_receipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_channels",
                table: "t_channels",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_channel_quota_entries",
                table: "t_channel_quota_entries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_channel_prices",
                table: "t_channel_prices",
                columns: new[] { "ChannelId", "ApplicationId", "PlanId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_bindings",
                table: "t_bindings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_batches",
                table: "t_batches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_backup_jobs",
                table: "t_backup_jobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_authorization_events",
                table: "t_authorization_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_audit_logs",
                table: "t_audit_logs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_user_tokens",
                table: "t_asp_net_user_tokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_users",
                table: "t_asp_net_users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_user_roles",
                table: "t_asp_net_user_roles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_user_logins",
                table: "t_asp_net_user_logins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_user_claims",
                table: "t_asp_net_user_claims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_roles",
                table: "t_asp_net_roles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_asp_net_role_claims",
                table: "t_asp_net_role_claims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_application_version_policies",
                table: "t_application_version_policies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_applications",
                table: "t_applications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_application_plans",
                table: "t_application_plans",
                columns: new[] { "ApplicationId", "PlanId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_api_credentials",
                table: "t_api_credentials",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_t_admin_recovery_codes",
                table: "t_admin_recovery_codes",
                columns: new[] { "UserId", "Digest" });

            migrationBuilder.AddForeignKey(
                name: "FK_t_admin_recovery_codes_t_asp_net_users_UserId",
                table: "t_admin_recovery_codes",
                column: "UserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_api_credentials_t_applications_ApplicationId",
                table: "t_api_credentials",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_api_credentials_t_channels_ChannelId",
                table: "t_api_credentials",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_application_plans_t_applications_ApplicationId",
                table: "t_application_plans",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_application_plans_t_plans_PlanId",
                table: "t_application_plans",
                column: "PlanId",
                principalTable: "t_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_application_version_policies_t_applications_ApplicationId",
                table: "t_application_version_policies",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_role_claims_t_asp_net_roles_RoleId",
                table: "t_asp_net_role_claims",
                column: "RoleId",
                principalTable: "t_asp_net_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_user_claims_t_asp_net_users_UserId",
                table: "t_asp_net_user_claims",
                column: "UserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_user_logins_t_asp_net_users_UserId",
                table: "t_asp_net_user_logins",
                column: "UserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_user_roles_t_asp_net_roles_RoleId",
                table: "t_asp_net_user_roles",
                column: "RoleId",
                principalTable: "t_asp_net_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_user_roles_t_asp_net_users_UserId",
                table: "t_asp_net_user_roles",
                column: "UserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_asp_net_user_tokens_t_asp_net_users_UserId",
                table: "t_asp_net_user_tokens",
                column: "UserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_batches_t_application_plans_ApplicationId_PlanId",
                table: "t_batches",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "t_application_plans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_batches_t_applications_ApplicationId",
                table: "t_batches",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_batches_t_channels_ChannelId",
                table: "t_batches",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_batches_t_customers_CustomerId",
                table: "t_batches",
                column: "CustomerId",
                principalTable: "t_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_batches_t_plans_PlanId",
                table: "t_batches",
                column: "PlanId",
                principalTable: "t_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_bindings_t_licenses_LicenseId",
                table: "t_bindings",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channel_prices_t_application_plans_ApplicationId_PlanId",
                table: "t_channel_prices",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "t_application_plans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channel_prices_t_channels_ChannelId",
                table: "t_channel_prices",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channel_prices_t_plans_PlanId",
                table: "t_channel_prices",
                column: "PlanId",
                principalTable: "t_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channel_quota_entries_t_channels_ChannelId",
                table: "t_channel_quota_entries",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channel_quota_entries_t_orders_OrderId",
                table: "t_channel_quota_entries",
                column: "OrderId",
                principalTable: "t_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_channels_t_asp_net_users_AccountId",
                table: "t_channels",
                column: "AccountId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_customers_t_applications_ApplicationId",
                table: "t_customers",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_customers_t_channels_ChannelId",
                table: "t_customers",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_license_freezes_t_licenses_LicenseId",
                table: "t_license_freezes",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_licenses_t_applications_ApplicationId",
                table: "t_licenses",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_licenses_t_batches_BatchId",
                table: "t_licenses",
                column: "BatchId",
                principalTable: "t_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_licenses_t_channels_ChannelId",
                table: "t_licenses",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_licenses_t_customers_CustomerId",
                table: "t_licenses",
                column: "CustomerId",
                principalTable: "t_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_notification_policies_t_applications_ApplicationId",
                table: "t_notification_policies",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_offline_licenses_t_applications_ApplicationId",
                table: "t_offline_licenses",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_offline_licenses_t_bindings_BindingId",
                table: "t_offline_licenses",
                column: "BindingId",
                principalTable: "t_bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_offline_licenses_t_licenses_LicenseId",
                table: "t_offline_licenses",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_order_histories_t_orders_OrderId",
                table: "t_order_histories",
                column: "OrderId",
                principalTable: "t_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_order_licenses_t_licenses_LicenseId",
                table: "t_order_licenses",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_order_licenses_t_orders_OrderId",
                table: "t_order_licenses",
                column: "OrderId",
                principalTable: "t_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_application_plans_ApplicationId_PlanId",
                table: "t_orders",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "t_application_plans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_applications_ApplicationId",
                table: "t_orders",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_batches_BatchId",
                table: "t_orders",
                column: "BatchId",
                principalTable: "t_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_channels_ChannelId",
                table: "t_orders",
                column: "ChannelId",
                principalTable: "t_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_customers_CustomerId",
                table: "t_orders",
                column: "CustomerId",
                principalTable: "t_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_licenses_RenewalLicenseId",
                table: "t_orders",
                column: "RenewalLicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_orders_t_plans_PlanId",
                table: "t_orders",
                column: "PlanId",
                principalTable: "t_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_portal_requests_t_bindings_BindingId",
                table: "t_portal_requests",
                column: "BindingId",
                principalTable: "t_bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_portal_requests_t_customers_CustomerId",
                table: "t_portal_requests",
                column: "CustomerId",
                principalTable: "t_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_portal_requests_t_licenses_LicenseId",
                table: "t_portal_requests",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_portal_requests_t_orders_OrderId",
                table: "t_portal_requests",
                column: "OrderId",
                principalTable: "t_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_portal_sessions_t_customers_CustomerId",
                table: "t_portal_sessions",
                column: "CustomerId",
                principalTable: "t_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_rebinding_events_t_bindings_BindingId",
                table: "t_rebinding_events",
                column: "BindingId",
                principalTable: "t_bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_rebinding_events_t_licenses_LicenseId",
                table: "t_rebinding_events",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_risk_policies_t_applications_ApplicationId",
                table: "t_risk_policies",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_role_definitions_t_asp_net_roles_RoleId",
                table: "t_role_definitions",
                column: "RoleId",
                principalTable: "t_asp_net_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_role_definitions_t_asp_net_users_LegacyUserId",
                table: "t_role_definitions",
                column: "LegacyUserId",
                principalTable: "t_asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_t_role_permissions_t_asp_net_roles_RoleId",
                table: "t_role_permissions",
                column: "RoleId",
                principalTable: "t_asp_net_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_t_sessions_t_bindings_BindingId",
                table: "t_sessions",
                column: "BindingId",
                principalTable: "t_bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_staff_notifications_t_operations_events_EventId",
                table: "t_staff_notifications",
                column: "EventId",
                principalTable: "t_operations_events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_trial_claims_t_applications_ApplicationId",
                table: "t_trial_claims",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_trial_claims_t_licenses_LicenseId",
                table: "t_trial_claims",
                column: "LicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_trial_claims_t_licenses_UpgradedToLicenseId",
                table: "t_trial_claims",
                column: "UpgradedToLicenseId",
                principalTable: "t_licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_trial_policies_t_applications_ApplicationId",
                table: "t_trial_policies",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_webhook_deliveries_t_operations_events_EventId",
                table: "t_webhook_deliveries",
                column: "EventId",
                principalTable: "t_operations_events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_webhook_deliveries_t_webhook_subscriptions_SubscriptionId",
                table: "t_webhook_deliveries",
                column: "SubscriptionId",
                principalTable: "t_webhook_subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_t_webhook_subscriptions_t_applications_ApplicationId",
                table: "t_webhook_subscriptions",
                column: "ApplicationId",
                principalTable: "t_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_t_admin_recovery_codes_t_asp_net_users_UserId",
                table: "t_admin_recovery_codes");

            migrationBuilder.DropForeignKey(
                name: "FK_t_api_credentials_t_applications_ApplicationId",
                table: "t_api_credentials");

            migrationBuilder.DropForeignKey(
                name: "FK_t_api_credentials_t_channels_ChannelId",
                table: "t_api_credentials");

            migrationBuilder.DropForeignKey(
                name: "FK_t_application_plans_t_applications_ApplicationId",
                table: "t_application_plans");

            migrationBuilder.DropForeignKey(
                name: "FK_t_application_plans_t_plans_PlanId",
                table: "t_application_plans");

            migrationBuilder.DropForeignKey(
                name: "FK_t_application_version_policies_t_applications_ApplicationId",
                table: "t_application_version_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_role_claims_t_asp_net_roles_RoleId",
                table: "t_asp_net_role_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_user_claims_t_asp_net_users_UserId",
                table: "t_asp_net_user_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_user_logins_t_asp_net_users_UserId",
                table: "t_asp_net_user_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_user_roles_t_asp_net_roles_RoleId",
                table: "t_asp_net_user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_user_roles_t_asp_net_users_UserId",
                table: "t_asp_net_user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_t_asp_net_user_tokens_t_asp_net_users_UserId",
                table: "t_asp_net_user_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_t_batches_t_application_plans_ApplicationId_PlanId",
                table: "t_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_t_batches_t_applications_ApplicationId",
                table: "t_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_t_batches_t_channels_ChannelId",
                table: "t_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_t_batches_t_customers_CustomerId",
                table: "t_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_t_batches_t_plans_PlanId",
                table: "t_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_t_bindings_t_licenses_LicenseId",
                table: "t_bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channel_prices_t_application_plans_ApplicationId_PlanId",
                table: "t_channel_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channel_prices_t_channels_ChannelId",
                table: "t_channel_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channel_prices_t_plans_PlanId",
                table: "t_channel_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channel_quota_entries_t_channels_ChannelId",
                table: "t_channel_quota_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channel_quota_entries_t_orders_OrderId",
                table: "t_channel_quota_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_t_channels_t_asp_net_users_AccountId",
                table: "t_channels");

            migrationBuilder.DropForeignKey(
                name: "FK_t_customers_t_applications_ApplicationId",
                table: "t_customers");

            migrationBuilder.DropForeignKey(
                name: "FK_t_customers_t_channels_ChannelId",
                table: "t_customers");

            migrationBuilder.DropForeignKey(
                name: "FK_t_license_freezes_t_licenses_LicenseId",
                table: "t_license_freezes");

            migrationBuilder.DropForeignKey(
                name: "FK_t_licenses_t_applications_ApplicationId",
                table: "t_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_licenses_t_batches_BatchId",
                table: "t_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_licenses_t_channels_ChannelId",
                table: "t_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_licenses_t_customers_CustomerId",
                table: "t_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_notification_policies_t_applications_ApplicationId",
                table: "t_notification_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_t_offline_licenses_t_applications_ApplicationId",
                table: "t_offline_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_offline_licenses_t_bindings_BindingId",
                table: "t_offline_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_offline_licenses_t_licenses_LicenseId",
                table: "t_offline_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_order_histories_t_orders_OrderId",
                table: "t_order_histories");

            migrationBuilder.DropForeignKey(
                name: "FK_t_order_licenses_t_licenses_LicenseId",
                table: "t_order_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_order_licenses_t_orders_OrderId",
                table: "t_order_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_application_plans_ApplicationId_PlanId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_applications_ApplicationId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_batches_BatchId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_channels_ChannelId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_customers_CustomerId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_licenses_RenewalLicenseId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_orders_t_plans_PlanId",
                table: "t_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_t_portal_requests_t_bindings_BindingId",
                table: "t_portal_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_t_portal_requests_t_customers_CustomerId",
                table: "t_portal_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_t_portal_requests_t_licenses_LicenseId",
                table: "t_portal_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_t_portal_requests_t_orders_OrderId",
                table: "t_portal_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_t_portal_sessions_t_customers_CustomerId",
                table: "t_portal_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_t_rebinding_events_t_bindings_BindingId",
                table: "t_rebinding_events");

            migrationBuilder.DropForeignKey(
                name: "FK_t_rebinding_events_t_licenses_LicenseId",
                table: "t_rebinding_events");

            migrationBuilder.DropForeignKey(
                name: "FK_t_risk_policies_t_applications_ApplicationId",
                table: "t_risk_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_t_role_definitions_t_asp_net_roles_RoleId",
                table: "t_role_definitions");

            migrationBuilder.DropForeignKey(
                name: "FK_t_role_definitions_t_asp_net_users_LegacyUserId",
                table: "t_role_definitions");

            migrationBuilder.DropForeignKey(
                name: "FK_t_role_permissions_t_asp_net_roles_RoleId",
                table: "t_role_permissions");

            migrationBuilder.DropForeignKey(
                name: "FK_t_sessions_t_bindings_BindingId",
                table: "t_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_t_staff_notifications_t_operations_events_EventId",
                table: "t_staff_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_t_trial_claims_t_applications_ApplicationId",
                table: "t_trial_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_t_trial_claims_t_licenses_LicenseId",
                table: "t_trial_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_t_trial_claims_t_licenses_UpgradedToLicenseId",
                table: "t_trial_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_t_trial_policies_t_applications_ApplicationId",
                table: "t_trial_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_t_webhook_deliveries_t_operations_events_EventId",
                table: "t_webhook_deliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_t_webhook_deliveries_t_webhook_subscriptions_SubscriptionId",
                table: "t_webhook_deliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_t_webhook_subscriptions_t_applications_ApplicationId",
                table: "t_webhook_subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_webhook_subscriptions",
                table: "t_webhook_subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_webhook_deliveries",
                table: "t_webhook_deliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_trial_policies",
                table: "t_trial_policies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_trial_claims",
                table: "t_trial_claims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_staff_notifications",
                table: "t_staff_notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_sessions",
                table: "t_sessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_role_permissions",
                table: "t_role_permissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_role_definitions",
                table: "t_role_definitions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_risk_policies",
                table: "t_risk_policies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_risk_events",
                table: "t_risk_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_rebinding_events",
                table: "t_rebinding_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_portal_sessions",
                table: "t_portal_sessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_portal_requests",
                table: "t_portal_requests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_plans",
                table: "t_plans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_orders",
                table: "t_orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_order_licenses",
                table: "t_order_licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_order_histories",
                table: "t_order_histories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_operations_events",
                table: "t_operations_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_operation_receipts",
                table: "t_operation_receipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_offline_licenses",
                table: "t_offline_licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_notification_policies",
                table: "t_notification_policies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_monitoring_samples",
                table: "t_monitoring_samples");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_monitoring_incidents",
                table: "t_monitoring_incidents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_licenses",
                table: "t_licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_license_freezes",
                table: "t_license_freezes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_customers",
                table: "t_customers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_commerce_receipts",
                table: "t_commerce_receipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_channels",
                table: "t_channels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_channel_quota_entries",
                table: "t_channel_quota_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_channel_prices",
                table: "t_channel_prices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_bindings",
                table: "t_bindings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_batches",
                table: "t_batches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_backup_jobs",
                table: "t_backup_jobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_authorization_events",
                table: "t_authorization_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_audit_logs",
                table: "t_audit_logs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_users",
                table: "t_asp_net_users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_user_tokens",
                table: "t_asp_net_user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_user_roles",
                table: "t_asp_net_user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_user_logins",
                table: "t_asp_net_user_logins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_user_claims",
                table: "t_asp_net_user_claims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_roles",
                table: "t_asp_net_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_asp_net_role_claims",
                table: "t_asp_net_role_claims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_applications",
                table: "t_applications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_application_version_policies",
                table: "t_application_version_policies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_application_plans",
                table: "t_application_plans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_api_credentials",
                table: "t_api_credentials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_t_admin_recovery_codes",
                table: "t_admin_recovery_codes");

            migrationBuilder.RenameTable(
                name: "t_webhook_subscriptions",
                newName: "WebhookSubscriptions");

            migrationBuilder.RenameTable(
                name: "t_webhook_deliveries",
                newName: "WebhookDeliveries");

            migrationBuilder.RenameTable(
                name: "t_trial_policies",
                newName: "TrialPolicies");

            migrationBuilder.RenameTable(
                name: "t_trial_claims",
                newName: "TrialClaims");

            migrationBuilder.RenameTable(
                name: "t_staff_notifications",
                newName: "StaffNotifications");

            migrationBuilder.RenameTable(
                name: "t_sessions",
                newName: "Sessions");

            migrationBuilder.RenameTable(
                name: "t_role_permissions",
                newName: "RolePermissions");

            migrationBuilder.RenameTable(
                name: "t_role_definitions",
                newName: "RoleDefinitions");

            migrationBuilder.RenameTable(
                name: "t_risk_policies",
                newName: "RiskPolicies");

            migrationBuilder.RenameTable(
                name: "t_risk_events",
                newName: "RiskEvents");

            migrationBuilder.RenameTable(
                name: "t_rebinding_events",
                newName: "RebindingEvents");

            migrationBuilder.RenameTable(
                name: "t_portal_sessions",
                newName: "PortalSessions");

            migrationBuilder.RenameTable(
                name: "t_portal_requests",
                newName: "PortalRequests");

            migrationBuilder.RenameTable(
                name: "t_plans",
                newName: "Plans");

            migrationBuilder.RenameTable(
                name: "t_orders",
                newName: "Orders");

            migrationBuilder.RenameTable(
                name: "t_order_licenses",
                newName: "OrderLicenses");

            migrationBuilder.RenameTable(
                name: "t_order_histories",
                newName: "OrderHistories");

            migrationBuilder.RenameTable(
                name: "t_operations_events",
                newName: "OperationsEvents");

            migrationBuilder.RenameTable(
                name: "t_operation_receipts",
                newName: "OperationReceipts");

            migrationBuilder.RenameTable(
                name: "t_offline_licenses",
                newName: "OfflineLicenses");

            migrationBuilder.RenameTable(
                name: "t_notification_policies",
                newName: "NotificationPolicies");

            migrationBuilder.RenameTable(
                name: "t_monitoring_samples",
                newName: "MonitoringSamples");

            migrationBuilder.RenameTable(
                name: "t_monitoring_incidents",
                newName: "MonitoringIncidents");

            migrationBuilder.RenameTable(
                name: "t_licenses",
                newName: "Licenses");

            migrationBuilder.RenameTable(
                name: "t_license_freezes",
                newName: "LicenseFreezes");

            migrationBuilder.RenameTable(
                name: "t_customers",
                newName: "Customers");

            migrationBuilder.RenameTable(
                name: "t_commerce_receipts",
                newName: "CommerceReceipts");

            migrationBuilder.RenameTable(
                name: "t_channels",
                newName: "Channels");

            migrationBuilder.RenameTable(
                name: "t_channel_quota_entries",
                newName: "ChannelQuotaEntries");

            migrationBuilder.RenameTable(
                name: "t_channel_prices",
                newName: "ChannelPrices");

            migrationBuilder.RenameTable(
                name: "t_bindings",
                newName: "Bindings");

            migrationBuilder.RenameTable(
                name: "t_batches",
                newName: "Batches");

            migrationBuilder.RenameTable(
                name: "t_backup_jobs",
                newName: "BackupJobs");

            migrationBuilder.RenameTable(
                name: "t_authorization_events",
                newName: "AuthorizationEvents");

            migrationBuilder.RenameTable(
                name: "t_audit_logs",
                newName: "AuditLogs");

            migrationBuilder.RenameTable(
                name: "t_asp_net_users",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "t_asp_net_user_tokens",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "t_asp_net_user_roles",
                newName: "AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "t_asp_net_user_logins",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "t_asp_net_user_claims",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "t_asp_net_roles",
                newName: "AspNetRoles");

            migrationBuilder.RenameTable(
                name: "t_asp_net_role_claims",
                newName: "AspNetRoleClaims");

            migrationBuilder.RenameTable(
                name: "t_applications",
                newName: "Applications");

            migrationBuilder.RenameTable(
                name: "t_application_version_policies",
                newName: "ApplicationVersionPolicies");

            migrationBuilder.RenameTable(
                name: "t_application_plans",
                newName: "ApplicationPlans");

            migrationBuilder.RenameTable(
                name: "t_api_credentials",
                newName: "ApiCredentials");

            migrationBuilder.RenameTable(
                name: "t_admin_recovery_codes",
                newName: "AdminRecoveryCodes");

            migrationBuilder.RenameIndex(
                name: "IX_t_webhook_subscriptions_ApplicationId",
                table: "WebhookSubscriptions",
                newName: "IX_WebhookSubscriptions_ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_t_webhook_deliveries_SubscriptionId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_t_webhook_deliveries_Status_NextAttemptAt",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_Status_NextAttemptAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_webhook_deliveries_EventId_SubscriptionId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_EventId_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_t_trial_claims_UpgradedToLicenseId",
                table: "TrialClaims",
                newName: "IX_TrialClaims_UpgradedToLicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_trial_claims_LicenseId",
                table: "TrialClaims",
                newName: "IX_TrialClaims_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_trial_claims_ApplicationId_DeviceDigest",
                table: "TrialClaims",
                newName: "IX_TrialClaims_ApplicationId_DeviceDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_staff_notifications_RecipientId_CreatedAt",
                table: "StaffNotifications",
                newName: "IX_StaffNotifications_RecipientId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_staff_notifications_EventId_RecipientId",
                table: "StaffNotifications",
                newName: "IX_StaffNotifications_EventId_RecipientId");

            migrationBuilder.RenameIndex(
                name: "IX_t_sessions_TokenDigest",
                table: "Sessions",
                newName: "IX_Sessions_TokenDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_sessions_LeaseExpiresAt",
                table: "Sessions",
                newName: "IX_Sessions_LeaseExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_sessions_BindingId",
                table: "Sessions",
                newName: "IX_Sessions_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_t_role_permissions_Permission_RoleId",
                table: "RolePermissions",
                newName: "IX_RolePermissions_Permission_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_t_role_definitions_LegacyUserId",
                table: "RoleDefinitions",
                newName: "IX_RoleDefinitions_LegacyUserId");

            migrationBuilder.RenameIndex(
                name: "IX_t_risk_events_LicenseId_Rule_WindowStart",
                table: "RiskEvents",
                newName: "IX_RiskEvents_LicenseId_Rule_WindowStart");

            migrationBuilder.RenameIndex(
                name: "IX_t_risk_events_ApplicationId_CreatedAt",
                table: "RiskEvents",
                newName: "IX_RiskEvents_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_rebinding_events_LicenseId_Generation_ChangedAt",
                table: "RebindingEvents",
                newName: "IX_RebindingEvents_LicenseId_Generation_ChangedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_rebinding_events_BindingId",
                table: "RebindingEvents",
                newName: "IX_RebindingEvents_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_sessions_TokenDigest",
                table: "PortalSessions",
                newName: "IX_PortalSessions_TokenDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_sessions_CustomerId_ExpiresAt",
                table: "PortalSessions",
                newName: "IX_PortalSessions_CustomerId_ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_requests_OrderId",
                table: "PortalRequests",
                newName: "IX_PortalRequests_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_requests_LicenseId",
                table: "PortalRequests",
                newName: "IX_PortalRequests_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_requests_CustomerId_RequestId",
                table: "PortalRequests",
                newName: "IX_PortalRequests_CustomerId_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_requests_BindingId",
                table: "PortalRequests",
                newName: "IX_PortalRequests_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_t_portal_requests_ApplicationId_ChannelId_CreatedAt",
                table: "PortalRequests",
                newName: "IX_PortalRequests_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_plans_CreatedAt_Id",
                table: "Plans",
                newName: "IX_Plans_CreatedAt_Id");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_RenewalLicenseId",
                table: "Orders",
                newName: "IX_Orders_RenewalLicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_PlanId",
                table: "Orders",
                newName: "IX_Orders_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_DeliveryExpiresAt",
                table: "Orders",
                newName: "IX_Orders_DeliveryExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_CustomerId",
                table: "Orders",
                newName: "IX_Orders_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_CreatedBy_RequestId",
                table: "Orders",
                newName: "IX_Orders_CreatedBy_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_ChannelId",
                table: "Orders",
                newName: "IX_Orders_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_BatchId",
                table: "Orders",
                newName: "IX_Orders_BatchId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_ApplicationId_PlanId",
                table: "Orders",
                newName: "IX_Orders_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_orders_ApplicationId_ChannelId_CreatedAt",
                table: "Orders",
                newName: "IX_Orders_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_order_licenses_LicenseId",
                table: "OrderLicenses",
                newName: "IX_OrderLicenses_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_order_histories_OrderId_CreatedAt",
                table: "OrderHistories",
                newName: "IX_OrderHistories_OrderId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_operations_events_DispatchedAt_CreatedAt",
                table: "OperationsEvents",
                newName: "IX_OperationsEvents_DispatchedAt_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_operations_events_ApplicationId_DeduplicationKey",
                table: "OperationsEvents",
                newName: "IX_OperationsEvents_ApplicationId_DeduplicationKey");

            migrationBuilder.RenameIndex(
                name: "IX_t_operation_receipts_ActorId_RequestId",
                table: "OperationReceipts",
                newName: "IX_OperationReceipts_ActorId_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_offline_licenses_Serial",
                table: "OfflineLicenses",
                newName: "IX_OfflineLicenses_Serial");

            migrationBuilder.RenameIndex(
                name: "IX_t_offline_licenses_LicenseId_IssuedAt",
                table: "OfflineLicenses",
                newName: "IX_OfflineLicenses_LicenseId_IssuedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_offline_licenses_BindingId",
                table: "OfflineLicenses",
                newName: "IX_OfflineLicenses_BindingId");

            migrationBuilder.RenameIndex(
                name: "IX_t_offline_licenses_ApplicationId_IssuedAt",
                table: "OfflineLicenses",
                newName: "IX_OfflineLicenses_ApplicationId_IssuedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_monitoring_samples_SampledAt",
                table: "MonitoringSamples",
                newName: "IX_MonitoringSamples_SampledAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_monitoring_incidents_StartedAt",
                table: "MonitoringIncidents",
                newName: "IX_MonitoringIncidents_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_monitoring_incidents_Kind",
                table: "MonitoringIncidents",
                newName: "IX_MonitoringIncidents_Kind");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_KeyDigest",
                table: "Licenses",
                newName: "IX_Licenses_KeyDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ExpiresAt",
                table: "Licenses",
                newName: "IX_Licenses_ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_CustomerId",
                table: "Licenses",
                newName: "IX_Licenses_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ChannelId",
                table: "Licenses",
                newName: "IX_Licenses_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_BatchId",
                table: "Licenses",
                newName: "IX_Licenses_BatchId");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ApplicationId_CreatedAt",
                table: "Licenses",
                newName: "IX_Licenses_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ApplicationId_ChannelId_CreatedAt",
                table: "Licenses",
                newName: "IX_Licenses_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ApplicationId_ActivatedAt",
                table: "Licenses",
                newName: "IX_Licenses_ApplicationId_ActivatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_licenses_ActivateBefore",
                table: "Licenses",
                newName: "IX_Licenses_ActivateBefore");

            migrationBuilder.RenameIndex(
                name: "IX_t_license_freezes_LicenseId_FrozenAt",
                table: "LicenseFreezes",
                newName: "IX_LicenseFreezes_LicenseId_FrozenAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_license_freezes_LicenseId",
                table: "LicenseFreezes",
                newName: "IX_LicenseFreezes_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_customers_ChannelId",
                table: "Customers",
                newName: "IX_Customers_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_t_customers_ApplicationId_ChannelId_CreatedAt",
                table: "Customers",
                newName: "IX_Customers_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_customers_AccessCodeDigest",
                table: "Customers",
                newName: "IX_Customers_AccessCodeDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_commerce_receipts_ActorId_Operation_RequestId",
                table: "CommerceReceipts",
                newName: "IX_CommerceReceipts_ActorId_Operation_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_channels_AccountId",
                table: "Channels",
                newName: "IX_Channels_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_t_channel_quota_entries_OrderId",
                table: "ChannelQuotaEntries",
                newName: "IX_ChannelQuotaEntries_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_t_channel_quota_entries_ChannelId_Kind_RequestId",
                table: "ChannelQuotaEntries",
                newName: "IX_ChannelQuotaEntries_ChannelId_Kind_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_channel_prices_PlanId",
                table: "ChannelPrices",
                newName: "IX_ChannelPrices_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_channel_prices_ApplicationId_PlanId",
                table: "ChannelPrices",
                newName: "IX_ChannelPrices_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_bindings_LicenseId_DeviceDigest",
                table: "Bindings",
                newName: "IX_Bindings_LicenseId_DeviceDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_bindings_DeviceDigest_LicenseId",
                table: "Bindings",
                newName: "IX_Bindings_DeviceDigest_LicenseId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_PlanId",
                table: "Batches",
                newName: "IX_Batches_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_CustomerId",
                table: "Batches",
                newName: "IX_Batches_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_CreatedBy_RequestId",
                table: "Batches",
                newName: "IX_Batches_CreatedBy_RequestId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_ChannelId",
                table: "Batches",
                newName: "IX_Batches_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_ApplicationId_PlanId",
                table: "Batches",
                newName: "IX_Batches_ApplicationId_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_ApplicationId_CreatedAt",
                table: "Batches",
                newName: "IX_Batches_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_batches_ApplicationId_ChannelId_CreatedAt",
                table: "Batches",
                newName: "IX_Batches_ApplicationId_ChannelId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_backup_jobs_CreatedAt",
                table: "BackupJobs",
                newName: "IX_BackupJobs_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_authorization_events_CreatedAt",
                table: "AuthorizationEvents",
                newName: "IX_AuthorizationEvents_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_authorization_events_ApplicationId_LicenseId_CreatedAt",
                table: "AuthorizationEvents",
                newName: "IX_AuthorizationEvents_ApplicationId_LicenseId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_audit_logs_TargetId_CreatedAt",
                table: "AuditLogs",
                newName: "IX_AuditLogs_TargetId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_audit_logs_CreatedAt",
                table: "AuditLogs",
                newName: "IX_AuditLogs_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_audit_logs_Category_CreatedAt",
                table: "AuditLogs",
                newName: "IX_AuditLogs_Category_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_audit_logs_ApplicationId_CreatedAt",
                table: "AuditLogs",
                newName: "IX_AuditLogs_ApplicationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_asp_net_user_roles_RoleId",
                table: "AspNetUserRoles",
                newName: "IX_AspNetUserRoles_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_t_asp_net_user_logins_UserId",
                table: "AspNetUserLogins",
                newName: "IX_AspNetUserLogins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_t_asp_net_user_claims_UserId",
                table: "AspNetUserClaims",
                newName: "IX_AspNetUserClaims_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_t_asp_net_role_claims_RoleId",
                table: "AspNetRoleClaims",
                newName: "IX_AspNetRoleClaims_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_t_applications_CreatedAt",
                table: "Applications",
                newName: "IX_Applications_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_t_applications_Code",
                table: "Applications",
                newName: "IX_Applications_Code");

            migrationBuilder.RenameIndex(
                name: "IX_t_application_plans_PlanId",
                table: "ApplicationPlans",
                newName: "IX_ApplicationPlans_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_t_api_credentials_SecretDigest",
                table: "ApiCredentials",
                newName: "IX_ApiCredentials_SecretDigest");

            migrationBuilder.RenameIndex(
                name: "IX_t_api_credentials_ChannelId",
                table: "ApiCredentials",
                newName: "IX_ApiCredentials_ChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_t_api_credentials_ApplicationId",
                table: "ApiCredentials",
                newName: "IX_ApiCredentials_ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeliveries",
                table: "WebhookDeliveries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TrialPolicies",
                table: "TrialPolicies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TrialClaims",
                table: "TrialClaims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StaffNotifications",
                table: "StaffNotifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RolePermissions",
                table: "RolePermissions",
                columns: new[] { "RoleId", "Permission" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoleDefinitions",
                table: "RoleDefinitions",
                column: "RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RiskPolicies",
                table: "RiskPolicies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RiskEvents",
                table: "RiskEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RebindingEvents",
                table: "RebindingEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PortalSessions",
                table: "PortalSessions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PortalRequests",
                table: "PortalRequests",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Plans",
                table: "Plans",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Orders",
                table: "Orders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderLicenses",
                table: "OrderLicenses",
                columns: new[] { "OrderId", "LicenseId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderHistories",
                table: "OrderHistories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OperationsEvents",
                table: "OperationsEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OperationReceipts",
                table: "OperationReceipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OfflineLicenses",
                table: "OfflineLicenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationPolicies",
                table: "NotificationPolicies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MonitoringSamples",
                table: "MonitoringSamples",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MonitoringIncidents",
                table: "MonitoringIncidents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LicenseFreezes",
                table: "LicenseFreezes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customers",
                table: "Customers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommerceReceipts",
                table: "CommerceReceipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Channels",
                table: "Channels",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ChannelQuotaEntries",
                table: "ChannelQuotaEntries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ChannelPrices",
                table: "ChannelPrices",
                columns: new[] { "ChannelId", "ApplicationId", "PlanId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Bindings",
                table: "Bindings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Batches",
                table: "Batches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BackupJobs",
                table: "BackupJobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuthorizationEvents",
                table: "AuthorizationEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserTokens",
                table: "AspNetUserTokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserRoles",
                table: "AspNetUserRoles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserLogins",
                table: "AspNetUserLogins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserClaims",
                table: "AspNetUserClaims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetRoles",
                table: "AspNetRoles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetRoleClaims",
                table: "AspNetRoleClaims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Applications",
                table: "Applications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApplicationVersionPolicies",
                table: "ApplicationVersionPolicies",
                column: "ApplicationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApplicationPlans",
                table: "ApplicationPlans",
                columns: new[] { "ApplicationId", "PlanId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApiCredentials",
                table: "ApiCredentials",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdminRecoveryCodes",
                table: "AdminRecoveryCodes",
                columns: new[] { "UserId", "Digest" });

            migrationBuilder.AddForeignKey(
                name: "FK_AdminRecoveryCodes_AspNetUsers_UserId",
                table: "AdminRecoveryCodes",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiCredentials_Applications_ApplicationId",
                table: "ApiCredentials",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiCredentials_Channels_ChannelId",
                table: "ApiCredentials",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationPlans_Applications_ApplicationId",
                table: "ApplicationPlans",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationPlans_Plans_PlanId",
                table: "ApplicationPlans",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationVersionPolicies_Applications_ApplicationId",
                table: "ApplicationVersionPolicies",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_ApplicationPlans_ApplicationId_PlanId",
                table: "Batches",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_Applications_ApplicationId",
                table: "Batches",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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
                name: "FK_Batches_Plans_PlanId",
                table: "Batches",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bindings_Licenses_LicenseId",
                table: "Bindings",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelPrices_ApplicationPlans_ApplicationId_PlanId",
                table: "ChannelPrices",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelPrices_Channels_ChannelId",
                table: "ChannelPrices",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelPrices_Plans_PlanId",
                table: "ChannelPrices",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelQuotaEntries_Channels_ChannelId",
                table: "ChannelQuotaEntries",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelQuotaEntries_Orders_OrderId",
                table: "ChannelQuotaEntries",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_AspNetUsers_AccountId",
                table: "Channels",
                column: "AccountId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Applications_ApplicationId",
                table: "Customers",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Channels_ChannelId",
                table: "Customers",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LicenseFreezes_Licenses_LicenseId",
                table: "LicenseFreezes",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Applications_ApplicationId",
                table: "Licenses",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Batches_BatchId",
                table: "Licenses",
                column: "BatchId",
                principalTable: "Batches",
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

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationPolicies_Applications_ApplicationId",
                table: "NotificationPolicies",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfflineLicenses_Applications_ApplicationId",
                table: "OfflineLicenses",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfflineLicenses_Bindings_BindingId",
                table: "OfflineLicenses",
                column: "BindingId",
                principalTable: "Bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfflineLicenses_Licenses_LicenseId",
                table: "OfflineLicenses",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderHistories_Orders_OrderId",
                table: "OrderHistories",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLicenses_Licenses_LicenseId",
                table: "OrderLicenses",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLicenses_Orders_OrderId",
                table: "OrderLicenses",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ApplicationPlans_ApplicationId_PlanId",
                table: "Orders",
                columns: new[] { "ApplicationId", "PlanId" },
                principalTable: "ApplicationPlans",
                principalColumns: new[] { "ApplicationId", "PlanId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Applications_ApplicationId",
                table: "Orders",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Batches_BatchId",
                table: "Orders",
                column: "BatchId",
                principalTable: "Batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Channels_ChannelId",
                table: "Orders",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Licenses_RenewalLicenseId",
                table: "Orders",
                column: "RenewalLicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Plans_PlanId",
                table: "Orders",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PortalRequests_Bindings_BindingId",
                table: "PortalRequests",
                column: "BindingId",
                principalTable: "Bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PortalRequests_Customers_CustomerId",
                table: "PortalRequests",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PortalRequests_Licenses_LicenseId",
                table: "PortalRequests",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PortalRequests_Orders_OrderId",
                table: "PortalRequests",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PortalSessions_Customers_CustomerId",
                table: "PortalSessions",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RebindingEvents_Bindings_BindingId",
                table: "RebindingEvents",
                column: "BindingId",
                principalTable: "Bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RebindingEvents_Licenses_LicenseId",
                table: "RebindingEvents",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RiskPolicies_Applications_ApplicationId",
                table: "RiskPolicies",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleDefinitions_AspNetRoles_RoleId",
                table: "RoleDefinitions",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleDefinitions_AspNetUsers_LegacyUserId",
                table: "RoleDefinitions",
                column: "LegacyUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_AspNetRoles_RoleId",
                table: "RolePermissions",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Bindings_BindingId",
                table: "Sessions",
                column: "BindingId",
                principalTable: "Bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffNotifications_OperationsEvents_EventId",
                table: "StaffNotifications",
                column: "EventId",
                principalTable: "OperationsEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrialClaims_Applications_ApplicationId",
                table: "TrialClaims",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrialClaims_Licenses_LicenseId",
                table: "TrialClaims",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrialClaims_Licenses_UpgradedToLicenseId",
                table: "TrialClaims",
                column: "UpgradedToLicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrialPolicies_Applications_ApplicationId",
                table: "TrialPolicies",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_OperationsEvents_EventId",
                table: "WebhookDeliveries",
                column: "EventId",
                principalTable: "OperationsEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_SubscriptionId",
                table: "WebhookDeliveries",
                column: "SubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookSubscriptions_Applications_ApplicationId",
                table: "WebhookSubscriptions",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
