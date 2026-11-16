using System.Security.Cryptography;
using System.Text.Json;
using ForeverSocial.Companion;

internal static class LoopbackBackgroundProbe
{
    internal static int Run(string root, string endpoint, string report)
    {
        var uri = BridgeFiles.Endpoint(endpoint);
        if (!uri.IsLoopback) throw new InvalidDataException("Integration probe is restricted to loopback");
        var aliceToken = Environment.GetEnvironmentVariable("FS_TEST_TOKEN") ?? throw new InvalidDataException("Missing synthetic credential");
        var berenToken = Environment.GetEnvironmentVariable("FS_TEST_OTHER_TOKEN") ?? throw new InvalidDataException("Missing synthetic credential");
        Application.SetHighDpiMode(HighDpiMode.SystemAware); Application.EnableVisualStyles();
        var config = Path.Combine(Path.GetDirectoryName(root)!, "companion-settings.json");
        new CompanionSettings { Folder = root, Endpoint = uri.AbsoluteUri }.Save(config);
        var exportFile = BridgeFiles.Exports(root).Single();
        var original = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exportFile)));
        var services = new CompanionServices { PollIntervalMilliseconds = 200, LoadCredential = (_, _) => aliceToken,
            SaveCredential = (_, _, _) => throw new InvalidOperationException("Enrollment is not part of this probe") };
        var payload = Path.Combine(root, "Interface/AddOns/ForeverSocial/Data/Companion.lua");
        var checks = new List<string>();
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        void PumpUntil(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(5); }
            if (!condition()) throw new TimeoutException("Loopback background integration timed out");
        }
        void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        WireRecord Post(int version, string text)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new("posts", JsonSerializer.SerializeToElement(new { id = "Beren-TestRealm@loopback", author = "Beren-TestRealm", created = now - 120,
                updated = now, version, category = "general", text, deleted = false }, Protocol.Json));
        }
        // Use a stable creation time across both updates, as the real server requires.
        var post = Post(1, "Remote post while minimized");
        using var remote = new SyncClient(uri);
        using (var first = new CompanionWindow(_ => null, config, services) { ShowInTaskbar = false, Opacity = 0 })
        {
            first.Show(); Application.DoEvents(); Complete(first.SynchronizeNowAsync());
            Require(File.Exists(payload), "initial authenticated sync writes a real snapshot: " + ((Label)first.Controls.Find("status",true).Single()).Text);
            Complete(remote.Sync(new Export("forever", "Beren-TestRealm", true, [post], Community: "TestRealm"), berenToken));
            ((CheckBox)first.Controls.Find("automatic", true).Single()).Checked = true;
            ((Button)first.Controls.Find("minimize", true).Single()).PerformClick();
            // Observe only completed writes; the cache is atomically replaced by
            // the worker while the UI message pump is running.
            PumpUntil(() => !first.IsSynchronizing && File.ReadAllText(payload).Contains("Remote post while minimized", StringComparison.Ordinal));
            Require(!first.Visible && first.TrayVisible, "real timer downloads a second account's post while minimized");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exportFile))) == original, "background polling leaves SavedVariables unchanged");
            first.Close();
        }
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(post.Record.GetRawText())!;
        fields["version"] = JsonSerializer.SerializeToElement(2); fields["text"] = JsonSerializer.SerializeToElement("Remote update after companion restart");
        Complete(remote.Sync(new Export("forever", "Beren-TestRealm", true, [post with { Record = JsonSerializer.SerializeToElement(fields, Protocol.Json) }], Community: "TestRealm"), berenToken));
        using (var second = new CompanionWindow(_ => null, config, services) { ShowInTaskbar = false, Opacity = 0 })
        {
            second.Show(); Application.DoEvents();
            Require(((CheckBox)second.Controls.Find("automatic", true).Single()).Checked, "automatic preference survives a complete window restart");
            PumpUntil(() => !second.IsSynchronizing && File.ReadAllText(payload).Contains("Remote update after companion restart", StringComparison.Ordinal));
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exportFile))) == original, "restart fetches new server data without a changed local export");
            second.Close();
        }
        Require(!File.ReadAllText(config).Contains(aliceToken, StringComparison.Ordinal) && !File.ReadAllText(payload).Contains(aliceToken, StringComparison.Ordinal), "credentials never enter settings or game files");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { generatedAtUTC = DateTimeOffset.UtcNow, passed = checks.Count, total = checks.Count,
            checks, realHttp = true, realFormsTimer = true, nativeGameTested = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{checks.Count}/{checks.Count} real HTTP background integration checks passed");
        return 0;
    }
}
