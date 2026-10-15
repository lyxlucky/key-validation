using KeyValidation.Domain;

namespace KeyValidation.Application;
public static class ApplicationPermissions
{
    public const string ReadLicenses = "licenses.read";
    public const string IssueLicenses = "licenses.issue";
    public const string ManageLicenses = "licenses.manage";
    public const string ReadReports = "reports.read";
    public const string ReadAudit = "audit.read";
    public const string Administrator = "platform.manage";
    public const string ManageIntegrations = "integrations.manage";
    public const string ManageCustomers = "customers.manage";
    public const string ManageOrders = "orders.manage";
    public static readonly PermissionDto[] Catalog = [new(ReadLicenses, "查看授权", "授权管理", "查看应用、卡种、卡密和设备授权信息。"), new(IssueLicenses, "发放授权", "授权管理", "创建卡密批次并查看发放结果。"), new(ManageLicenses, "管理授权", "授权管理", "修改授权、续期、冻结和解除设备绑定。"), new(ReadReports, "查看报表", "运营管理", "查看概览及授权统计报表。"), new(ReadAudit, "查看日志", "系统管理", "查看权限范围内的操作日志和登录日志。"), new("risk.review", "复核风险", "运营管理", "查看并复核授权异常风险。"), new("customers.manage", "管理客户", "客户与渠道", "管理客户资料。"), new("orders.manage", "管理订单", "客户与渠道", "管理业务订单。"), new(ManageIntegrations, "管理集成", "运营管理", "配置 Webhook 并管理投递。"), new(Administrator, "平台管理", "系统管理", "管理账号、角色、平台策略和运维；业务权限仍须分别授予。")];
    public static readonly string[] All = Catalog.Select(x => x.Code).ToArray();
}
