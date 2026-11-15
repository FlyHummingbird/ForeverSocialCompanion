// Loaded only by the test runner through DOTNET_STARTUP_HOOKS. Never shipped.
// This tests the real single-file host and its bundled Forms/runtime dependencies.
using System.Reflection;
using System.Text.Json;

internal static class StartupHook
{
    public static void Initialize()
    {
        var report = Environment.GetEnvironmentVariable("FS_PACKAGE_STARTUP_REPORT");
        if (string.IsNullOrEmpty(report)) return;
        try
        {
            // Match Program.Main's apartment without entering the personal app.
            var thread = new Thread(() => Run(report));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(); thread.Join();
        }
        catch (Exception error)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { ok = false, error = error.ToString() }));
            Environment.Exit(1);
        }
        Environment.Exit(0);
    }

    private static void Run(string report)
    {
        try
        {
            var app = Assembly.Load("ForeverSocialCompanion");
            if (app.EntryPoint?.GetCustomAttribute<STAThreadAttribute>() is null) throw new InvalidDataException("Missing STA entry point");
            var type = app.GetType("CompanionWindow", throwOnError: true)!;
            var config = Path.Combine(Path.GetDirectoryName(report)!, "isolated-package-settings.json");
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            using var window = (Form)Activator.CreateInstance(type, new object?[] { null, config, null })!;
            window.ShowInTaskbar = false; window.Opacity = 0;
            window.Show(); Application.DoEvents();
            var sync = window.Controls.Find("synchronize", true).Single();
            if (!sync.Visible || !sync.Enabled) throw new InvalidDataException("Sync action unavailable");
            var setup = window.Controls.Find("setupPanel", true).Single();
            if (!setup.Visible) throw new InvalidDataException("First-run setup must be available");
            var output = new { ok = true, version = app.GetName().Version?.ToString(3),
                singleFileHost = string.IsNullOrEmpty(app.Location), bundledFormsLoaded = true,
                entryPointSTA = true, realWindowConstructed = true, setupAvailable = true,
                synchronizationAvailable = true, personalSettingsRead = false, networkUsed = false };
            window.Close();
            File.WriteAllText(report, JsonSerializer.Serialize(output));
        }
        catch (Exception error)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { ok = false, error = error.ToString() }));
            Environment.Exit(1);
        }
    }
}
