using KeyValidation.Domain;

namespace KeyValidation.Application;
public static class OfflineLicenseLimits
{
    public const int MaximumHours = 168;
    public const int DefaultHours = 24;
    public const string Limitations = "离线设备无法即时获知撤销、冻结、禁用或解绑；最长延迟为许可证剩余有效期。";
}
