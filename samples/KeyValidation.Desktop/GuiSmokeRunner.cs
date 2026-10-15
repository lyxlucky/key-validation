using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using KeyValidation.Client;

namespace KeyValidation.Desktop;

/// <summary>Explicit diagnostic entry point. Uses the same form, events, coordinator and transport as the interactive app.</summary>
internal static class GuiSmokeRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(DesktopOptions options)
    {
        var output = options.SmokeOutput!;
        Directory.CreateDirectory(output);
        var started = DateTimeOffset.UtcNow;
        var cases = new List<SmokeCase>();
        var activeCase = "fixture_preflight";
        var exitCode = 1;
        var diagnostics = new SafeDiagnostics();
        MainForm? form = null;
        try
        {
            var profiles = ReadManifest(options.SmokeManifest!);
            var stateDirectory = options.StateDirectory ?? Path.Combine(output, "isolated-state");
            var interactive = Path.GetFullPath(LocalPreferences.DefaultDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var state = Path.GetFullPath(stateDirectory).TrimEnd(Path.DirectorySeparatorChar);
            Require(!state.Equals(interactive, StringComparison.OrdinalIgnoreCase) && !state.StartsWith(interactive + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "SMOKE_REQUIRES_ISOLATED_STATE");
            Directory.CreateDirectory(stateDirectory);
            var picks = new SmokeFilePickers();
            var coordinator = new SessionCoordinator(p => DesktopTransport.Create(p, diagnostics), new FileTrustedTimeStore(Path.Combine(stateDirectory, "trusted-time.json")));
            form = new MainForm(coordinator, diagnostics, new DemoProfile { DeviceId = LocalPreferences.LoadDeviceId(stateDirectory) }, picks)
            {
                ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0)
            };
            var hiddenForm = form;
            var sensitiveInputs = new HashSet<string>(StringComparer.Ordinal);
            form.Shown += async (_, _) =>
            {
                try
                {
                    async Task RunCase(string name, Func<Task> action)
                    {
                        activeCase = name;
                        var begin = Stopwatch.GetTimestamp();
                        await action();
                        cases.Add(new(name, "passed", "OK", (long)Stopwatch.GetElapsedTime(begin).TotalMilliseconds));
                        File.AppendAllText(Path.Combine(output, "smoke-progress.log"), DateTimeOffset.UtcNow.ToString("O") + " " + name + " passed\n");
                    }
                    async Task Load(string name)
                    {
                        picks.Enqueue(FilePurpose.Profile, profiles[name]);
                        await Click(hiddenForm, "loadProfile");
                        foreach (var field in new[] { "licenseKey", "deviceId" })
                        {
                            var value = Control<TextBox>(hiddenForm, field).Text;
                            if (value.Length > 3) sensitiveInputs.Add(value);
                        }
                        Require(!coordinator.Snapshot().Authorized, "PROFILE_AUTO_ACTIVATED");
                    }
                    void Authorized(bool exportAllowed, bool trial = false)
                    {
                        var snapshot = coordinator.Snapshot();
                        Require(snapshot.Authorized && snapshot.CanExport == exportAllowed && snapshot.IsTrial == trial, "UNEXPECTED_AUTHORIZATION_STATE");
                        Require(Control<Button>(hiddenForm, "basicAction").Enabled && Control<Button>(hiddenForm, "exportCsv").Enabled == exportAllowed, "PROTECTED_BUTTON_STATE_MISMATCH");
                    }
                    async Task ProtectedCsv(string fileName)
                    {
                        var path = Path.Combine(output, fileName);
                        picks.Enqueue(FilePurpose.ProtectedCsv, path);
                        await Click(hiddenForm, "exportCsv");
                        Require(File.Exists(path) && File.ReadAllText(path) == "item,quantity\n演示项目,1\n", "PROTECTED_CSV_NOT_WRITTEN");
                    }

                    await RunCase("explicit_profile_load", async () => { await Load("online"); Require(diagnostics.NetworkRequests == 0, "PROFILE_LOAD_USED_NETWORK"); });
                    await RunCase("formal_activation", async () => { await Click(hiddenForm, "activate"); Authorized(true); });
                    await RunCase("protected_basic_button", async () => { await Click(hiddenForm, "basicAction"); var result = coordinator.Snapshot().LastResult; Require(result.Code == "BASIC_COMPLETED" && result.DemoValue == 42, "BASIC_ACTION_NOT_EXECUTED"); });
                    await RunCase("protected_csv_button", () => ProtectedCsv("online-demo.csv"));
                    await RunCase("public_version_button", async () =>
                    {
                        await Click(hiddenForm, "checkVersion"); var policy = coordinator.Snapshot().Version;
                        Require(policy?.MinimumVersion == "2.1.0" && policy.LatestVersion == "2.10.0", "PUBLIC_VERSION_CONTRACT_MISMATCH");
                    });
                    await RunCase("manual_heartbeat_button", async () => { await Click(hiddenForm, "heartbeat"); Require(coordinator.Snapshot().LastResult.Code == "HEARTBEAT_CONFIRMED", "HEARTBEAT_NOT_CONFIRMED"); });
                    await RunCase("layout_1024x768", () => Capture(hiddenForm, output, 1024, 768));
                    await RunCase("layout_1440x900", () => Capture(hiddenForm, output, 1440, 900));
                    await RunCase("automatic_heartbeat_toggle_and_timer", async () =>
                    {
                        var toggle = Control<CheckBox>(hiddenForm, "automaticHeartbeat");
                        toggle.Checked = false; Require(!coordinator.Snapshot().AutomaticHeartbeat, "AUTO_PAUSE_FAILED");
                        var prior = diagnostics.Snapshot().Count(x => x.Operation == DiagnosticOperation.Heartbeat && x.StatusCode == 200);
                        var priorResults = diagnostics.Snapshot().Count(x => x.Operation == DiagnosticOperation.Heartbeat && x.Code == "HEARTBEAT_CONFIRMED");
                        toggle.Checked = true;
                        await WaitUntil(() => diagnostics.Snapshot().Count(x => x.Operation == DiagnosticOperation.Heartbeat && x.StatusCode == 200) > prior &&
                            diagnostics.Snapshot().Count(x => x.Operation == DiagnosticOperation.Heartbeat && x.Code == "HEARTBEAT_CONFIRMED") > priorResults, TimeSpan.FromSeconds(90));
                        toggle.Checked = false;
                        Authorized(true);
                    });
                    await RunCase("checked_logout_button", async () =>
                    {
                        await Click(hiddenForm, "logout"); var result = coordinator.Snapshot();
                        Require(!result.Authorized && result.LastResult.Code == "LOGOUT_CONFIRMED", "REMOTE_LOGOUT_NOT_CONFIRMED");
                    });
                    await RunCase("checked_device_release_button", async () =>
                    {
                        await Click(hiddenForm, "activate"); Authorized(true); await Click(hiddenForm, "releaseDevice");
                        Require(!coordinator.Snapshot().Authorized && coordinator.Snapshot().LastResult.Code == "DEVICE_RELEASED", "DEVICE_RELEASE_NOT_CONFIRMED");
                    });
                    await RunCase("basic_plan_feature_gate", async () =>
                    {
                        await Load("basic"); await Click(hiddenForm, "activate"); Authorized(false);
                        await Click(hiddenForm, "basicAction"); Require(coordinator.Snapshot().LastResult.Code == "BASIC_COMPLETED", "BASIC_PLAN_DENIED_BASIC");
                        await Click(hiddenForm, "logout");
                    });
                    await RunCase("trial_button", async () => { await Load("trial"); await Click(hiddenForm, "activateTrial"); Authorized(false, trial: true); });
                    await RunCase("same_device_trial_upgrade", async () => { await Click(hiddenForm, "activate"); Authorized(true); await Click(hiddenForm, "logout"); });
                    await RunCase("actual_version_426_rejection", async () =>
                    {
                        await Load("outdated"); await Click(hiddenForm, "activate"); var snapshot = coordinator.Snapshot();
                        Require(!snapshot.Authorized && snapshot.LastResult.Code == "CLIENT_UPGRADE_REQUIRED" && snapshot.Version?.MinimumVersion == "2.1.0", "VERSION_REJECTION_NOT_ENFORCED");
                        Require(diagnostics.Snapshot().Any(x => x.StatusCode == 426), "NO_REAL_426_RESPONSE");
                    });

                    long offlineNetworkCount = diagnostics.NetworkRequests;
                    DemoProfile? offlineProfile = null;
                    await RunCase("offline_real_file_import_and_signature", async () =>
                    {
                        await Load("offline"); offlineProfile = await DemoProfile.LoadAsync(profiles["offline"]);
                        Require(offlineProfile.OfflineLicensePath is not null && offlineProfile.TrustedKeysPath is not null, "OFFLINE_FIXTURE_PATHS_MISSING");
                        picks.Enqueue(FilePurpose.OfflineLicense, offlineProfile.OfflineLicensePath!); await Click(hiddenForm, "chooseOfflineLicense");
                        picks.Enqueue(FilePurpose.TrustedKeys, offlineProfile.TrustedKeysPath!); await Click(hiddenForm, "chooseTrustedKeys");
                        await Click(hiddenForm, "verifyOffline"); Authorized(true);
                        Require(diagnostics.NetworkRequests == offlineNetworkCount, "OFFLINE_USED_NETWORK");
                    });
                    await RunCase("offline_protected_buttons", async () => { await Click(hiddenForm, "basicAction"); await ProtectedCsv("offline-demo.csv"); });
                    await RunCase("offline_wrong_device_rejected", async () =>
                    {
                        await Click(hiddenForm, "clearAuthorization"); Control<TextBox>(hiddenForm, "deviceId").Text = "mismatch-" + Guid.NewGuid().ToString("N");
                        await Click(hiddenForm, "verifyOffline");
                        Require(!coordinator.Snapshot().Authorized && coordinator.Snapshot().LastResult.Code == "OFFLINE_DEVICE_MISMATCH", "OFFLINE_IDENTITY_NOT_ENFORCED");
                    });
                    await RunCase("offline_tampered_file_rejected", async () =>
                    {
                        await Load("offline");
                        var temporary = Path.Combine(stateDirectory, "tampered-" + Guid.NewGuid().ToString("N") + ".kglicense");
                        try
                        {
                            var parts = File.ReadAllText(offlineProfile!.OfflineLicensePath!).Trim().Split('.');
                            Require(parts.Length == 3, "OFFLINE_FIXTURE_FORMAT_INVALID");
                            parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
                            File.WriteAllText(temporary, string.Join('.', parts));
                            picks.Enqueue(FilePurpose.OfflineLicense, temporary); await Click(hiddenForm, "chooseOfflineLicense");
                            await Click(hiddenForm, "verifyOffline");
                            Require(!coordinator.Snapshot().Authorized && coordinator.Snapshot().LastResult.Code == "OFFLINE_LICENSE_INVALID", "OFFLINE_TAMPER_NOT_ENFORCED");
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    });
                    await RunCase("offline_zero_http_requests", () => { Require(diagnostics.NetworkRequests == offlineNetworkCount, "OFFLINE_USED_NETWORK"); return Task.CompletedTask; });
                    await RunCase("safe_bounded_diagnostic_export", async () =>
                    {
                        Control<TabControl>(hiddenForm, "mainTabs").SelectedIndex = 1;
                        var path = Path.Combine(output, "safe-diagnostics.csv"); picks.Enqueue(FilePurpose.Diagnostics, path);
                        await Click(hiddenForm, "exportDiagnostics");
                        Require(diagnostics.Snapshot().Length <= SafeDiagnostics.Capacity && File.Exists(path), "DIAGNOSTIC_EXPORT_FAILED");
                        var contents = File.ReadAllText(path);
                        Require(sensitiveInputs.All(value => !contents.Contains(value, StringComparison.Ordinal)), "DIAGNOSTIC_SECRET_LEAK");
                    });
                    exitCode = 0;
                }
                catch (Exception error)
                {
                    cases.Add(new(activeCase, "failed", error is SmokeFailure failure ? failure.Code : "SMOKE_UNEXPECTED_FAILURE", 0));
                }
                finally
                {
                    coordinator.Clear();
                    try { WriteResults(output, started, cases, diagnostics, exitCode); }
                    catch { exitCode = 2; }
                    finally { hiddenForm.Close(); }
                }
            };
            Application.ThreadException += (_, _) =>
            {
                exitCode = 1;
                cases.Add(new(activeCase, "failed", "SMOKE_UI_UNHANDLED_FAILURE", 0));
                try { WriteResults(output, started, cases, diagnostics, exitCode); }
                finally { hiddenForm.Close(); }
            };
            Application.Run(form);
        }
        catch (Exception error)
        {
            cases.Add(new(activeCase, "failed", error is SmokeFailure failure ? failure.Code : "SMOKE_STARTUP_FAILED", 0));
            WriteResults(output, started, cases, diagnostics, 1);
        }
        finally { form?.Dispose(); }
        return exitCode;
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        Require(File.Exists(path), "FIXTURE_MANIFEST_MISSING");
        using var stream = File.OpenRead(path);
        Require(stream.Length is > 0 and <= 65536, "FIXTURE_MANIFEST_INVALID");
        using var json = JsonDocument.Parse(stream);
        Require(json.RootElement.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object, "FIXTURE_MANIFEST_INVALID");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "online", "basic", "trial", "outdated", "offline" })
        {
            Require(profiles.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String, "FIXTURE_PROFILE_MISSING");
            var file = value.GetString()!;
            Require(Path.IsPathFullyQualified(file) && File.Exists(file), "FIXTURE_PROFILE_PATH_MISSING");
            result.Add(name, file);
        }
        return result;
    }

    private static async Task Click(MainForm form, string name)
    {
        var button = Control<Button>(form, name);
        Require(button.Enabled && button.Visible, "SMOKE_BUTTON_UNAVAILABLE");
        button.PerformClick();
        await WaitUntil(() => !form.OperationPending, TimeSpan.FromSeconds(25));
        await Task.Delay(50);
    }
    private static T Control<T>(MainForm form, string name) where T : Control =>
        form.Controls.Find(name, true).OfType<T>().SingleOrDefault() ?? throw new SmokeFailure("SMOKE_CONTROL_MISSING");

    private static async Task Capture(MainForm form, string output, int width, int height)
    {
        Control<CheckBox>(form, "showSecrets").Checked = false;
        Require(Control<TextBox>(form, "licenseKey").UseSystemPasswordChar && Control<TextBox>(form, "deviceId").UseSystemPasswordChar, "SCREENSHOT_INPUTS_NOT_MASKED");
        form.Size = new Size(width, height); form.PerformLayout(); await Task.Delay(200);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(output, "desktop-" + width + "x" + height + ".png"), ImageFormat.Png);
        var layout = new List<object>();
        var allReachable = true;
        foreach (var name in new[] { "loadProfile", "clearAuthorization", "activate", "activateTrial", "heartbeat", "logout", "releaseDevice", "checkVersion", "basicAction", "exportCsv" })
        {
            var button = Control<Button>(form, name);
            var origin = form.PointToClient(button.Parent!.PointToScreen(button.Location));
            var reachable = button.Visible && form.ClientRectangle.Contains(new Rectangle(origin, button.Size));
            allReachable &= reachable;
            layout.Add(new { control = name, reachable, x = origin.X, y = origin.Y, width = button.Width, height = button.Height, button.TabIndex, button.AccessibleName });
            Require(!string.IsNullOrWhiteSpace(button.AccessibleName) && button.TabStop, "ACCESSIBILITY_CONTROL_UNNAMED");
        }
        var metadata = Control<RichTextBox>(form, "authorizationStatus");
        var minimumMetadataHeight = Math.Max(100, metadata.Font.Height * 4);
        var metadataReadable = metadata.ClientSize.Height >= minimumMetadataHeight;
        layout.Add(new { control = "authorizationStatus", height = metadata.ClientSize.Height, minimumHeight = minimumMetadataHeight, readable = metadataReadable });
        var deviceInput = Control<TextBox>(form, "deviceId");
        var deviceCaption = deviceInput.Parent!.Controls.OfType<Label>().Single();
        var captionReadable = deviceCaption.Height >= deviceCaption.PreferredHeight && deviceCaption.Bottom <= deviceInput.Top &&
            deviceInput.Parent.ClientRectangle.Contains(deviceInput.Bounds);
        layout.Add(new { control = "deviceIdLabel", height = deviceCaption.Height, preferredHeight = deviceCaption.PreferredHeight,
            bottom = deviceCaption.Bottom, inputTop = deviceInput.Top, readable = captionReadable });
        var progressLabel = Control<Label>(form, "operationProgress");
        var progressBounds = progressLabel.RectangleToScreen(progressLabel.ClientRectangle);
        var progressFullyVisible = progressLabel.Visible;
        var progressAncestors = new List<object>();
        for (var ancestor = progressLabel.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            var viewport = ancestor.RectangleToScreen(ancestor.ClientRectangle);
            var viewportOrigin = form.PointToClient(viewport.Location);
            var containsProgress = ancestor.Visible && viewport.Contains(progressBounds);
            progressFullyVisible &= containsProgress;
            progressAncestors.Add(new { control = ancestor.Name, type = ancestor.GetType().Name,
                x = viewportOrigin.X, y = viewportOrigin.Y, width = viewport.Width, height = viewport.Height,
                displayHeight = ancestor.DisplayRectangle.Height, minimumHeight = ancestor.MinimumSize.Height,
                paddingTop = ancestor.Padding.Top, paddingBottom = ancestor.Padding.Bottom, containsProgress });
        }
        var progressReadable = progressLabel.ClientSize.Height >= progressLabel.Font.Height && progressFullyVisible;
        var progressOrigin = form.PointToClient(progressBounds.Location);
        layout.Add(new { control = "operationProgress", x = progressOrigin.X, y = progressOrigin.Y, width = progressLabel.Width,
            height = progressLabel.ClientSize.Height, fontHeight = progressLabel.Font.Height, fullyVisible = progressFullyVisible,
            readable = progressReadable, ancestors = progressAncestors });
        File.WriteAllText(Path.Combine(output, "layout-" + width + "x" + height + ".json"), JsonSerializer.Serialize(layout, Json));
        Require(allReachable, "LAYOUT_PRIMARY_ACTION_CLIPPED");
        Require(metadataReadable, "LAYOUT_METADATA_TOO_SHORT");
        Require(captionReadable, "LAYOUT_DEVICE_CAPTION_CLIPPED");
        Require(progressReadable, "LAYOUT_PROGRESS_CLIPPED");
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(deadline) >= timeout) throw new SmokeFailure("SMOKE_TIMEOUT");
            await Task.Delay(50);
        }
    }

    private static void WriteResults(string output, DateTimeOffset started, IReadOnlyList<SmokeCase> cases, SafeDiagnostics diagnostics, int exitCode)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "smoke-results.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, evidenceKind = "real-live-api-and-winforms", status = exitCode == 0 ? "passed" : "failed", startedAt = started,
            finishedAt = DateTimeOffset.UtcNow, passed = cases.Count(x => x.Status == "passed"), failed = cases.Count(x => x.Status == "failed"),
            skipped = 0, httpRequests = diagnostics.NetworkRequests, cases
        }, Json));
        diagnostics.ExportCsv(Path.Combine(output, "transport-evidence.csv"));
    }
    public static void WriteStartupFailure(string output)
    {
        try { WriteResults(output, DateTimeOffset.UtcNow, [new("startup", "failed", "SMOKE_STARTUP_FAILED", 0)], new SafeDiagnostics(), 2); }
        catch { }
    }
    private static void Require(bool condition, string code) { if (!condition) throw new SmokeFailure(code); }
    private sealed class SmokeFailure(string code) : Exception(code) { public string Code { get; } = code; }
    private sealed record SmokeCase(string Name, string Status, string Code, long DurationMilliseconds);
    private sealed class SmokeFilePickers : IFilePickers
    {
        private readonly Queue<(FilePurpose Purpose, string Path)> queued = new();
        public void Enqueue(FilePurpose purpose, string path) => queued.Enqueue((purpose, path));
        public string? Select(IWin32Window owner, FilePurpose purpose)
        {
            Require(queued.TryDequeue(out var value) && value.Purpose == purpose, "SMOKE_FILE_SELECTION_MISSING");
            return value.Path;
        }
    }
}
