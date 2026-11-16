using System.Net;
using System.Text.Json;
using ForeverSocial.Companion;

internal sealed class CompanionWindow : Form
{
    private static bool German => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
    private static string T(string de, string en) => German ? de : en;
    private static readonly Color Gold = Color.FromArgb(226, 184, 83), Muted = Color.FromArgb(170, 181, 193);
    private readonly TextBox folder = new() { Name = "gameFolder", Dock = DockStyle.Fill };
    private readonly TextBox endpoint = new() { Name = "endpoint", Dock = DockStyle.Fill };
    private readonly ComboBox characters = new() { Name = "characters", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly Label status = Message("status"), lastSync = Message("lastSync"), exportTime = Message("exportTime"), importHint = Message("importHint"), automaticState = Message("automaticState");
    private readonly Button browse = Button("browse", T("WoW-Spielordner auswählen…", "Choose WoW game folder…"));
    private readonly Button synchronize = Button("synchronize", T("Jetzt synchronisieren", "Synchronize now"));
    private readonly Button register = Button("connect", T("Charakter verbinden", "Connect character"));
    private readonly Label characterSummary = Message("characterSummary");
    private readonly LinkLabel setupToggle = new() { Name="setupToggle",AutoSize=true,LinkColor=Gold,ActiveLinkColor=Color.White,Margin=new Padding(0,8,18,4) };
    private readonly LinkLabel historyToggle = new() { Name="historyToggle",Text=T("Letzte Aktivitäten","Recent activity"),AutoSize=true,LinkColor=Gold,ActiveLinkColor=Color.White,Margin=new Padding(0,8,0,4) };
    private readonly TableLayoutPanel setupPanel,historyPanel;
    private readonly Button minimize = Button("minimize", T("Im Hintergrund weiterlaufen", "Keep running in background"));
    private readonly CheckBox automatic = new() { Name = "automatic", Text = T("Automatisch alle 30 Sekunden synchronisieren", "Synchronize automatically every 30 seconds"), AutoSize = true };
    private readonly ListBox activity = new() { Name = "activity", Height = 82, Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer timer;
    private readonly NotifyIcon tray = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem trayAutomatic;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SyncSession session = new();
    private readonly CompanionServices services;
    private readonly Func<IWin32Window, string?> chooseFolder;
    private readonly string config;
    private CompanionSettings preferences = new();
    private List<Export> exports = [];
    private bool busy, initializing = true, closing, resourcesDisposed;
    private int failures;
    private DateTimeOffset nextPoll;

    internal bool IsSynchronizing => busy;
    internal DateTimeOffset NextPoll => nextPoll;
    internal bool PollingEnabled => timer.Enabled;
    internal bool TrayVisible => tray.Visible;
    internal Task SynchronizeNowAsync() => Act(false);

    public CompanionWindow(Func<IWin32Window, string?>? folderPicker = null, string? settingsFile = null, CompanionServices? dependencies = null)
    {
        services = dependencies ?? new();
        config = settingsFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForeverSocialCompanion", "settings.json");
        chooseFolder = folderPicker ?? ChooseFolder;
        timer = new() { Interval = Math.Max(50, services.PollIntervalMilliseconds) };
        try { preferences = CompanionSettings.Load(config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException) { }
        var version = typeof(CompanionWindow).Assembly.GetName().Version?.ToString(3);
        Text = "Forever Social Companion " + version;
#if !SIGNED_RELEASE
        Text += T(" · unsignierte Testversion", " · unsigned test build");
#endif
        Size = new Size(860, 590); MinimumSize = new Size(780, 480); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 21, 27); ForeColor = Color.Gainsboro; Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(22) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(layout); Controls.Add(scroll);
        var heading = new Label { Text = "Forever Social Companion", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), ForeColor = Gold, Margin = new Padding(0, 0, 0, 3) };
        layout.Controls.Add(heading);
        layout.Controls.Add(new Label { Text = T("Deine Community. Auch zwischen den Spielsitzungen verbunden.", "Your community, connected between game sessions."), AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 15) });
        var overview = Card(T("SYNCHRONISIERUNG", "SYNCHRONIZATION")); layout.Controls.Add(overview);
        characterSummary.ForeColor=Gold;characterSummary.Font=new Font(Font.FontFamily,12,FontStyle.Bold);overview.Controls.Add(characterSummary);
        characters.Dock=DockStyle.Top;overview.Controls.Add(characters);
        status.Font = new Font(Font, FontStyle.Bold); status.ForeColor = Color.LightSteelBlue;
        foreach (var label in new[] { status, lastSync, exportTime, importHint }) overview.Controls.Add(label);
        lastSync.Text = T("Letzter erfolgreicher Abgleich: noch keiner in dieser Sitzung", "Last successful sync: none in this session");
        importHint.Text = T("Neue Inhalte im Spiel mit /reload laden.", "Use /reload in game to load new content.");
        importHint.ForeColor = Gold;
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 9, 0, 6) };
        actions.Controls.Add(synchronize); actions.Controls.Add(minimize); overview.Controls.Add(actions);
        overview.Controls.Add(automatic); overview.Controls.Add(automaticState);
        var links=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0)};links.Controls.Add(setupToggle);links.Controls.Add(historyToggle);overview.Controls.Add(links);
        var connection = setupPanel = Card(T("VERBINDUNG EINRICHTEN", "CONNECTION SETUP"));setupPanel.Name="setupPanel"; layout.Controls.Add(connection);
        var fields = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); connection.Controls.Add(fields);
        void Row(string title, Control control, int row)
        {
            fields.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(0, 6, 0, 0), ForeColor = Muted }, 0, row);
            control.Margin = new Padding(0, 3, 0, 5); fields.Controls.Add(control, 1, row);
        }
        Row(T("Spielordner", "Game folder"), folder, 0); fields.Controls.Add(browse, 1, 1);
        Row(T("Community-Server", "Community server"), endpoint, 2);
        fields.Controls.Add(register, 1, 3);
        connection.Controls.Add(new Label { AutoSize=true, MaximumSize=new Size(740,0), ForeColor=Muted,
            Text=T("Im Addon /fs companion öffnen, Freigabe aktivieren und /reload ausführen. Danach hier „Jetzt abgleichen“ wählen. Ein Einladungscode ist nicht erforderlich.",
                "Open /fs companion in the addon, enable sharing and use /reload. Then choose Sync now here. No invitation code is required.") });
        var recent = historyPanel = Card(T("LETZTE AKTIVITÄTEN", "RECENT ACTIVITY"));historyPanel.Name="historyPanel"; recent.Controls.Add(activity); layout.Controls.Add(recent);historyPanel.Visible=false;
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Margin = new Padding(0, 9, 0, 0), ForeColor = Muted,
            Text = T("Nur freigegebene öffentliche Inhalte werden übertragen. Private Notizen, Begegnungen und Blocklisten bleiben privat. Minimieren lässt die App im Infobereich weiterlaufen; X beendet sie.", "Only permitted public content is transferred. Private notes, encounters and block lists stay private. Minimize to keep the app running in the system tray; X exits it.") });
        foreach (var box in new TextBox[] { folder, endpoint }) { box.BackColor = Color.FromArgb(32, 37, 46); box.ForeColor = ForeColor; box.BorderStyle = BorderStyle.FixedSingle; }
        characters.BackColor = activity.BackColor = Color.FromArgb(32, 37, 46); characters.ForeColor = activity.ForeColor = ForeColor;
        trayMenu.Items.Add(T("Öffnen", "Open"), null, (_, _) => RestoreWindow());
        trayMenu.Items.Add(T("Jetzt synchronisieren", "Synchronize now"), null, async (_, _) => await Act(false));
        trayAutomatic = new(T("Automatisch synchronisieren", "Synchronize automatically")) { CheckOnClick = true };
        trayAutomatic.Click += (_, _) => automatic.Checked = trayAutomatic.Checked; trayMenu.Items.Add(trayAutomatic);
        trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add(T("Beenden", "Exit"), null, (_, _) => Close());
        tray.Icon = Icon; tray.ContextMenuStrip = trayMenu; tray.Text = "Forever Social Companion";
        tray.DoubleClick += (_, _) => RestoreWindow();
        Shown += (_, _) => tray.Visible = true;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && !closing) { tray.Visible = true; Hide(); } };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        setupToggle.LinkClicked+=(_,_)=>ShowSetup(!setupPanel.Visible);
        historyToggle.LinkClicked+=(_,_)=>{historyPanel.Visible=!historyPanel.Visible;historyToggle.Text=historyPanel.Visible?T("Aktivitäten schließen","Hide activity"):T("Letzte Aktivitäten","Recent activity");};
        browse.Click += (_, _) => BrowseFolder();
        folder.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; ReadCharacters(); } };
        folder.Leave += (_, _) => { if (!busy) ReadCharacters(); };
        folder.TextChanged += (_, _) => { exports = []; characters.Items.Clear(); };
        characters.SelectedIndexChanged += (_, _) => {if(Selected() is {} selected)characterSummary.Text=selected.Actor+" · "+(selected.Client=="forever"?"Forever":"Retail"); if (!initializing && automatic.Checked && !busy) SavePreferences(); };
        register.Click += async (_, _) => await Act(true); synchronize.Text=T("Jetzt abgleichen","Sync now");synchronize.Click += async (_, _) => await Act(true);
        automatic.CheckedChanged += (_, _) => AutomaticChanged();
        timer.Tick += async (_, _) => await PollAsync();
        Activated += (_, _) => { if (!busy && !closing) ReadCharacters(); };
        folder.Text = preferences.Folder; endpoint.Text = preferences.Endpoint;
        ReadCharacters(); automatic.Checked = preferences.Automatic; initializing = false;
        AutomaticChanged(false);
        ShowSetup(Selected() is null);
    }

    internal void ShowSetup(bool visible)
    {
        setupPanel.Visible=visible;setupToggle.Text=visible?T("Einrichtung schließen","Close setup"):T("Einrichtung ändern","Change setup");
        if(visible && Height<800)Height=Math.Min(880,Screen.FromControl(this).WorkingArea.Height-35);
        else if(!visible && Height>650)Height=590;
    }

    private static Label Message(string name) => new() { Name = name, AutoSize = true, MaximumSize = new Size(740, 0), Margin = new Padding(0, 4, 0, 4), ForeColor = Muted };
    private static Button Button(string name, string text) => new() { Name = name, Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(61, 49, 28), ForeColor = Gold, Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 0, 9, 3) };
    private static TableLayoutPanel Card(string title)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(15), Margin = new Padding(0, 0, 0, 12), BackColor = Color.FromArgb(25, 29, 36) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Gold, Margin = new Padding(0, 0, 0, 9) });
        return card;
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private void AutomaticChanged(bool persist = true)
    {
        timer.Enabled = automatic.Checked; trayAutomatic.Checked = automatic.Checked;
        nextPoll = services.Now().AddMilliseconds(timer.Interval); failures = 0;
        automaticState.Text = automatic.Checked
            ? T("Automatik aktiv – neue Serverdaten werden auch ohne eigene Änderungen abgefragt.", "Automatic sync active – server updates are checked even without local changes.")
            : T("Automatik pausiert. Du kannst jederzeit manuell synchronisieren.", "Automatic sync paused. You can synchronize manually at any time.");
        if (!initializing && persist) SavePreferences();
    }

    private void SavePreferences()
    {
        var selected = Selected();
        preferences = preferences with { Folder = folder.Text, Endpoint = endpoint.Text, Automatic = automatic.Checked,
            Actor = selected?.Actor ?? preferences.Actor, Client = selected?.Client ?? preferences.Client };
        try { preferences.Save(config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { automaticState.Text = T("Einstellungen konnten nicht gespeichert werden. Die aktuelle Sitzung läuft weiter.", "Settings could not be saved. The current session continues."); }
    }

    private string? ChooseFolder(IWin32Window owner)
    {
        using var picker = new FolderBrowserDialog { Description = T("_forever_ oder _retail_ auswählen", "Select _forever_ or _retail_"), UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (Directory.Exists(folder.Text)) picker.SelectedPath = folder.Text;
        return picker.ShowDialog(owner) == DialogResult.OK ? picker.SelectedPath : null;
    }
    private void BrowseFolder()
    {
        if (busy) return;
        try { var selected = chooseFolder(this); if (selected is not null) { folder.Text = selected; ReadCharacters(); } }
        catch (Exception e) when (e is ThreadStateException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { SetStatus(T("Der Ordnerdialog konnte nicht geöffnet werden. Trage den Pfad zu _forever_ oder _retail_ direkt ein und drücke Enter." , "The folder dialog could not open. Enter the path to _forever_ or _retail_ directly and press Enter.")); folder.Focus(); }
    }
    private Export? Selected() => characters.SelectedIndex >= 0 && characters.SelectedIndex < exports.Count ? exports[characters.SelectedIndex] : null;
    private bool ReadCharacters()
    {
        var selected = Selected(); var actor = selected?.Actor ?? preferences.Actor; var client = selected?.Client ?? preferences.Client;
        var wasInitializing = initializing;
        initializing = true; exports = []; characters.Items.Clear();
        try
        {
            var root = BridgeFiles.ClientRoot(folder.Text);
            exports = BridgeFiles.Exports(root).SelectMany(p => SavedVariables.Exports(SavedVariables.Parse(File.ReadAllText(p)))).Where(e => e.Enabled).ToList();
            foreach (var item in exports) characters.Items.Add(item.Actor + " · " + item.Client);
            if (characters.Items.Count > 0) { var index = exports.FindIndex(e => e.Actor == actor && e.Client == client); characters.SelectedIndex = index >= 0 ? index : 0; }
            var current = Selected();
            characters.Visible=exports.Count>1;
            characterSummary.Text=current is null?T("Noch kein Charakter freigegeben","No character shared yet"):current.Actor+" · "+(current.Client=="forever"?"Forever":"Retail");
            if (current is null) { SetStatus(T("Noch kein freigegebener Export. Im Addon /fs companion und /reload verwenden.", "No permitted export yet. Use /fs companion and /reload in the addon.")); exportTime.Text = ""; return false; }
            exportTime.Text = T("Von WoW zuletzt gespeichert: ", "Last saved by WoW: ") + DateTimeOffset.FromUnixTimeSeconds(current.SavedAt).ToLocalTime().ToString("g");
            if (status.Text.Length == 0 || selected is null || selected.Actor != current.Actor || selected.Client != current.Client)
                SetStatus(T("Charakter gefunden. Verbinden oder jetzt synchronisieren.", "Character found. Connect or synchronize now."));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException)
        { exports = []; characters.Items.Clear(); SetStatus(T("Spielordner und gespeicherten Addon-Export prüfen. Den Unterordner _forever_ oder _retail_ auswählen.", "Check the game folder and saved addon export. Select the _forever_ or _retail_ subfolder.")); return false; }
        finally { initializing = wasInitializing; }
    }

    internal async Task PollAsync()
    {
        if (!automatic.Checked || busy || closing || services.Now() < nextPoll) return;
        await Act(false);
    }
    private void SetStatus(string message, bool error = false)
    {
        status.Text = message; status.ForeColor = error ? Color.LightSalmon : Color.LightSteelBlue;
        var tip = "Forever Social · " + message; tray.Text = tip[..Math.Min(63, tip.Length)];
    }
    private void Log(string message)
    {
        activity.Items.Insert(0, services.Now().ToLocalTime().ToString("HH:mm:ss") + "  " + message);
        while (activity.Items.Count > 30) activity.Items.RemoveAt(activity.Items.Count - 1);
    }
    private async Task Act(bool enroll)
    {
        if (busy || closing) return;
        busy = true;
        foreach (var control in new Control[] { synchronize, register, browse, folder, endpoint, characters }) control.Enabled = false;
        try
        {
            if (!ReadCharacters() || Selected() is not { } export) {ShowSetup(true);return;}
            var root = BridgeFiles.ClientRoot(folder.Text); var uri = BridgeFiles.Endpoint(endpoint.Text);
            var token = services.LoadCredential(uri, export);
            // Validate the installed data slot before creating a server account.
            BridgeFiles.SnapshotPath(root);
            using var client = services.CreateClient(uri);
            if (enroll && token is null)
            {
                SetStatus(T("Charakter wird verbunden…", "Connecting character…"));
                var credential = await client.Enroll(export, lifetime.Token);
                token = credential.Token; services.SaveCredential(uri, export, token);
            }
            if (token is null) { SetStatus(T("Diesen Charakter zuerst mit „Jetzt abgleichen“ verbinden.", "Connect this character with Sync now first.")); return; }
            SetStatus(T("Community-Daten werden abgeglichen…", "Synchronizing community data…"));
            var outcome = await session.Synchronize(root, export, uri, token, client, lifetime.Token);
            if (closing) return;
            failures = 0;
            var time = services.Now().ToLocalTime().ToString("g");
            lastSync.Text = T("Letzter erfolgreicher Abgleich: ", "Last successful sync: ") + time;
            SetStatus(outcome.DataChanged ? T("Neue Datendatei bereit – im Spiel /reload verwenden.", "Updated data file ready – use /reload in game.") : T("Abgleich erfolgreich – keine neuen Inhalte.", "Sync successful – no new content."));
            if (outcome.DataChanged) importHint.Text = T("Daten bereit: ", "Data ready: ") + time + T(" · Im Spiel /reload verwenden.", " · Use /reload in game.");
            Log(German ? $"{outcome.Snapshot.Accepted} Änderungen gespeichert · {outcome.Snapshot.Records.Count} Datensätze empfangen" : $"{outcome.Snapshot.Accepted} changes saved · {outcome.Snapshot.Records.Count} records received");
            SavePreferences();
            ShowSetup(false);
            try{
                var migration=LegacyMigration.Retire(root,Path.Combine(Path.GetDirectoryName(config)!,"Backups"));
                if(migration.Removed)Log(T("Alte Daten-Erweiterung gesichert und ins Hauptaddon übernommen.","Legacy data addon backed up and merged into the main addon."));
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){Log(T("Abgleich erfolgreich; alte Daten-Erweiterung konnte noch nicht entfernt werden.","Sync succeeded; the legacy data addon could not yet be removed."));}
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException)
        {
            if (closing) return;
            failures = Math.Min(failures + 1, 5);
            var reason = e switch
            {
                AddonUpdateRequiredException => T("Bitte Forever Social im Spiel auf Version 1.3.2 oder neuer aktualisieren.","Please update the in-game Forever Social addon to version 1.3.2 or newer."),
                HttpRequestException { StatusCode: HttpStatusCode.BadRequest } => T("Der Server hat die Daten abgelehnt. Addon-Export und Serverversion prüfen.", "The server rejected the data. Check the addon export and server version."),
                HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => T("Zugang abgelaufen oder ungültig. Charakter erneut verbinden oder Serverbetreiber kontaktieren.", "Access expired or invalid. Reconnect the character or contact the server administrator."),
                HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => T("Zugriff verweigert. Vorhandenen Zugang und Serverfreigabe prüfen.", "Access denied. Check the existing credential and server permissions."),
                HttpRequestException { StatusCode: HttpStatusCode.Conflict } => T("Dieser Charakter ist bereits verbunden. Vorhandenen Zugang nutzen oder Serverbetreiber kontaktieren.", "This character is already connected. Use the existing credential or contact the server administrator."),
                HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => T("Der Server begrenzt Anfragen. Der nächste Versuch erfolgt mit Abstand.", "The server is limiting requests. The next attempt will be delayed."),
                HttpRequestException or OperationCanceledException => T("Server nicht erreichbar. Verbindung wird beim nächsten Abgleich erneut geprüft.", "Server unreachable. The connection will be checked again on the next sync."),
                UnauthorizedAccessException or System.ComponentModel.Win32Exception => T("Datei- oder Zugangsdatenzugriff verweigert. Ordnerberechtigungen prüfen.", "File or credential access denied. Check folder permissions."),
                _ => T("Daten konnten nicht abgeglichen werden. Spielordner, Export und Serverdaten prüfen.", "Data could not be synchronized. Check the game folder, export and server data.")
            };
            SetStatus(reason, true); Log(reason);
        }
        finally
        {
            busy = false;
            nextPoll = services.Now().AddMilliseconds(Math.Min(300000, (double)timer.Interval * (1 << Math.Max(0, failures - 1))));
            if (!closing)
            {
                foreach (var control in new Control[] { synchronize, register, browse, folder, endpoint, characters }) control.Enabled = true;
                if (automatic.Checked && failures > 0) automaticState.Text = T("Automatischer neuer Versuch ab ", "Automatic retry from ") + nextPoll.ToLocalTime().ToString("HH:mm:ss");
                else if (automatic.Checked) automaticState.Text = T("Automatik aktiv · nächster Abgleich ab ", "Automatic sync active · next check from ") + nextPoll.ToLocalTime().ToString("HH:mm:ss");
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true; timer.Stop(); lifetime.Cancel(); base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; closing = true; lifetime.Cancel(); timer.Dispose(); tray.Dispose(); trayMenu.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
