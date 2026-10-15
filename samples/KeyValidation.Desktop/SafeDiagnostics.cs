using System.Globalization;
using System.Text;

namespace KeyValidation.Desktop;

public enum DiagnosticOperation { State, Activate, Trial, Heartbeat, Logout, ReleaseDevice, Version, OfflineVerify, BasicAction, ExportCsv, ProfileLoad }
public sealed record DiagnosticEntry(DateTimeOffset Timestamp, DiagnosticOperation Operation, string Method, string Path,
    int? StatusCode, long DurationMilliseconds, string Code);

/// <summary>Accepts operation identifiers and allowlisted codes, never arbitrary text, headers or bodies.</summary>
public sealed class SafeDiagnostics
{
    private readonly object gate = new();
    private readonly Queue<DiagnosticEntry> entries = new();
    private long revision;
    private long networkRequests;
    public const int Capacity = 200;
    public long Revision { get { lock (gate) return revision; } }
    public long NetworkRequests { get { lock (gate) return networkRequests; } }
    internal void RequestStarted() { lock (gate) networkRequests++; }
    public DiagnosticEntry[] Snapshot() { lock (gate) return entries.ToArray(); }

    public void Record(DiagnosticOperation operation, int? status = null, TimeSpan duration = default, string code = "IDLE")
    {
        if (!Enum.IsDefined(operation)) operation = DiagnosticOperation.State;
        var (method, path) = Descriptor(operation);
        var entry = new DiagnosticEntry(DateTimeOffset.UtcNow, operation, method, path,
            status is >= 100 and <= 599 ? status : null, (long)Math.Clamp(duration.TotalMilliseconds, 0, 3600000), ErrorCatalog.SafeCode(code));
        lock (gate)
        {
            entries.Enqueue(entry);
            while (entries.Count > Capacity) entries.Dequeue();
            revision++;
        }
    }
    public void Clear() { lock (gate) { entries.Clear(); revision++; } }
    public void ExportCsv(string path)
    {
        var output = new StringBuilder("utc,operation,method,path,status,duration_ms,code\r\n");
        foreach (var entry in Snapshot())
            output.Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(entry.Operation).Append(',')
                .Append(entry.Method).Append(',').Append(entry.Path).Append(',').Append(entry.StatusCode).Append(',')
                .Append(entry.DurationMilliseconds).Append(',').Append(entry.Code).Append("\r\n");
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
    }
    public static string OperationLabel(DiagnosticOperation operation) => operation switch
    {
        DiagnosticOperation.Activate => "卡密激活", DiagnosticOperation.Trial => "领取试用", DiagnosticOperation.Heartbeat => "心跳",
        DiagnosticOperation.Logout => "退出会话", DiagnosticOperation.ReleaseDevice => "释放设备", DiagnosticOperation.Version => "公开版本",
        DiagnosticOperation.OfflineVerify => "离线验证", DiagnosticOperation.BasicAction => "基础操作", DiagnosticOperation.ExportCsv => "CSV 导出",
        DiagnosticOperation.ProfileLoad => "载入配置", _ => "本地状态"
    };
    internal static DiagnosticOperation OperationForRoute(string? path) => path switch
    {
        "/api/client/licenses/activate" => DiagnosticOperation.Activate,
        "/api/client/trials/activate" => DiagnosticOperation.Trial,
        "/api/client/sessions/heartbeat" => DiagnosticOperation.Heartbeat,
        "/api/client/sessions/logout" => DiagnosticOperation.Logout,
        "/api/client/device-bindings/release" => DiagnosticOperation.ReleaseDevice,
        { } value when value.StartsWith("/api/client/applications/", StringComparison.Ordinal) && value.EndsWith("/version", StringComparison.Ordinal) => DiagnosticOperation.Version,
        _ => DiagnosticOperation.State
    };
    private static (string Method, string Path) Descriptor(DiagnosticOperation operation) => operation switch
    {
        DiagnosticOperation.Activate => ("POST", "/api/client/licenses/activate"),
        DiagnosticOperation.Trial => ("POST", "/api/client/trials/activate"),
        DiagnosticOperation.Heartbeat => ("POST", "/api/client/sessions/heartbeat"),
        DiagnosticOperation.Logout => ("POST", "/api/client/sessions/logout"),
        DiagnosticOperation.ReleaseDevice => ("POST", "/api/client/device-bindings/release"),
        DiagnosticOperation.Version => ("GET", "/api/client/applications/{appCode}/version"),
        _ => ("LOCAL", "-")
    };
}
