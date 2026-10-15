namespace KeyValidation.Desktop;

internal enum FilePurpose { Profile, OfflineLicense, TrustedKeys, ProtectedCsv, Diagnostics }
internal interface IFilePickers
{
    string? Select(IWin32Window owner, FilePurpose purpose);
}

internal sealed class NativeFilePickers : IFilePickers
{
    public string? Select(IWin32Window owner, FilePurpose purpose)
    {
        if (purpose is FilePurpose.ProtectedCsv or FilePurpose.Diagnostics)
        {
            using var dialog = new SaveFileDialog
            {
                Title = purpose == FilePurpose.ProtectedCsv ? "保存演示 CSV" : "导出安全诊断", Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = purpose == FilePurpose.ProtectedCsv ? "keygate-demo.csv" : "keygate-diagnostics.csv", AddExtension = true,
                OverwritePrompt = true, CheckPathExists = true
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }
        using var open = new OpenFileDialog
        {
            Title = purpose switch { FilePurpose.Profile => "载入测试配置（不会自动激活）", FilePurpose.OfflineLicense => "选择离线许可证", _ => "选择独立可信公钥 JSON" },
            Filter = purpose == FilePurpose.OfflineLicense ? "KeyGate 离线许可证 (*.kglicense)|*.kglicense|所有文件 (*.*)|*.*" : "JSON 文件 (*.json)|*.json",
            CheckFileExists = true, Multiselect = false
        };
        return open.ShowDialog(owner) == DialogResult.OK ? open.FileName : null;
    }
}
