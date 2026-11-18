using System.Security.Cryptography;
using System.Text.Json;
using ForeverSocial.Companion;

internal sealed partial class CompanionWindow
{
    private TableLayoutPanel accessPanel=null!;
    private readonly Button exportAccess=Button("exportAccess",T("Zugang sichern…","Back up access…"));
    private readonly Button importAccess=Button("importAccess",T("Zugang übernehmen…","Import access…"));
    private readonly Button deleteAccount=Button("deleteAccount",T("Serverkonto löschen…","Delete server account…"));
    private readonly Button reconnect=Button("reconnectAccess",T("Nach Löschung erneut verbinden…","Reconnect after deletion…"));
    private readonly LinkLabel accessToggle=new(){Name="accessToggle",AutoSize=true,LinkColor=Gold,ActiveLinkColor=Color.White,Margin=new Padding(18,8,0,4)};
    internal Task ManageAccessAsync(string action)=>ManageAccess(action);
    private static string AccessScope(Uri uri,Export export)=>uri.AbsoluteUri+"|"+export.Client+"|"+export.Actor;
    private void BuildAccessPanel(TableLayoutPanel layout,FlowLayoutPanel links)
    {
        accessToggle.Text=T("Zugang verwalten","Manage access");links.Controls.Add(accessToggle);
        accessPanel=Card(T("ZUGANG VERWALTEN","MANAGE ACCESS"));accessPanel.Name="accessPanel";layout.Controls.Add(accessPanel);accessPanel.Visible=false;
        accessPanel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(740,0),ForeColor=Muted,Text=T("Gilt für den ausgewählten Charakter und Community-Server. Für einen zweiten PC den bestehenden Zugang mit eigenem Passwort sichern und dort übernehmen. Das bestätigt keine Blizzard-Identität.","Applies to the selected character and community server. Back up existing access with your own password and import it on another PC. This does not verify Blizzard identity.")});
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true};actions.Controls.Add(exportAccess);actions.Controls.Add(importAccess);actions.Controls.Add(deleteAccount);actions.Controls.Add(reconnect);accessPanel.Controls.Add(actions);
        accessPanel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(740,0),ForeColor=Muted,Text=T("Löschen entfernt dieses Serverkonto und dessen eigene Inhalte. Lokale Spieldaten und Kopien bei anderen Spielern bleiben erhalten. Automatik wird pausiert; erneute Veröffentlichung erfordert bewusstes Verbinden.","Deletion removes this server account and its authored content. Local game data and other players' copies remain. Automatic sync is paused; publishing again requires deliberate reconnection.")});
        accessToggle.LinkClicked+=(_,_)=>{accessPanel.Visible=!accessPanel.Visible;accessToggle.Text=accessPanel.Visible?T("Zugangsverwaltung schließen","Close access management"):T("Zugang verwalten","Manage access");};
        exportAccess.Click+=async(_,_)=>await ManageAccess("export");importAccess.Click+=async(_,_)=>await ManageAccess("import");
        deleteAccount.Click+=async(_,_)=>await ManageAccess("delete");reconnect.Click+=async(_,_)=>await ManageAccess("reconnect");
    }
    private void SetAccessBusy(bool value)
    {
        foreach(var control in new Control[]{synchronize,register,browse,folder,endpoint,characters,exportAccess,importAccess,deleteAccount,reconnect,automatic})control.Enabled=!value;
        trayAutomatic.Enabled=!value;
    }
    private async Task ManageAccess(string action)
    {
        if(busy || closing || action is not ("export" or "import" or "delete" or "reconnect"))return;
        busy=true;SetAccessBusy(true);
        try{
            if(!ReadCharacters() || Selected() is not {} export){ShowSetup(true);return;}
            var root=BridgeFiles.ClientRoot(folder.Text);BridgeFiles.SnapshotPath(root);
            var uri=BridgeFiles.Endpoint(endpoint.Text);var scope=AccessScope(uri,export);
            if(action=="reconnect"){
                if(!preferences.RetiredScopes.Contains(scope,StringComparer.Ordinal)){SetStatus(T("Dieser Zugang ist nicht als gelöscht markiert. Vorhandenen Zugang nutzen oder übernehmen.","This account is not marked deleted. Use or import its existing access."));return;}
                if(!services.ConfirmAccess(this,T("Für ","For ")+export.Actor+" · "+uri.Host+T(" erneut ein Serverkonto anlegen? Bereits gespeicherte eigene Inhalte werden beim nächsten Abgleich wieder veröffentlicht."," create a server account again? Saved authored content will be published again on the next sync.")))return;
                preferences=preferences with{RetiredScopes=preferences.RetiredScopes.Where(s=>s!=scope).ToList()};SavePreferences();
                SetStatus(T("Erneute Verbindung freigegeben. Jetzt abgleichen wählen.","Reconnection enabled. Choose Sync now."));return;
            }
            var token=services.LoadCredential(uri,export);
            using var client=services.CreateClient(uri);
            if(action=="import"){
                var path=services.AccessFile(this,false);if(path is null)return;
                BridgeFiles.RejectLink(path);
                if(new FileInfo(path).Length>AccessTransfer.MaxBytes)throw new InvalidDataException("Transfer too large");
                var text=File.ReadAllText(path);var password=services.AccessPassword(this,false);if(password is null)return;
                var access=await Task.Run(()=>AccessTransfer.Unprotect(text,password),lifetime.Token);
                AccessTransfer.Match(access,uri,export);
                await client.InspectAccount(export,access.Token,lifetime.Token);
                if(token is not null && token!=access.Token && !services.ConfirmAccess(this,T("Den vorhandenen lokalen Zugang für ","Replace existing local access for ")+export.Actor+" · "+uri.Host+"?"))return;
                services.SaveCredential(uri,export,access.Token);
                preferences=preferences with{RetiredScopes=preferences.RetiredScopes.Where(s=>s!=scope).ToList()};session=new();SavePreferences();
                SetStatus(T("Zugang übernommen. Jetzt abgleichen wählen.","Access imported. Choose Sync now."));Log(T("Verschlüsselten Zugang übernommen.","Imported encrypted access."));return;
            }
            if(token is null){SetStatus(T("Für diesen Charakter ist noch kein Zugang gespeichert.","No access is saved for this character yet."));return;}
            if(action=="export"){
                await client.InspectAccount(export,token,lifetime.Token);
                var password=services.AccessPassword(this,true);if(password is null)return;
                var path=services.AccessFile(this,true);if(path is null)return;
                var fullPath=Path.GetFullPath(path);var gamePrefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(fullPath.StartsWith(gamePrefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Keep access backups outside the game installation");
                var access=new TransferredAccess(uri.AbsoluteUri,export.Client,export.Actor,export.Community,token);
                var encrypted=await Task.Run(()=>AccessTransfer.Protect(access,password),lifetime.Token);
                BridgeFiles.WriteAtomic(fullPath,encrypted);
                SetStatus(T("Zugang verschlüsselt gesichert. Passwort getrennt aufbewahren.","Encrypted access backed up. Keep the password separately."));Log(T("Verschlüsselte Zugangsdatei erstellt.","Created encrypted access file."));return;
            }
            if(!services.ConfirmAccess(this,T("Serverkonto von ","Delete the server account for ")+export.Actor+" · "+uri.Host+T(" und alle eigenen Serverinhalte endgültig löschen? Lokale Daten und fremde Kopien bleiben erhalten."," and all its authored server content permanently? Local data and other players' copies remain.")))return;
            automatic.Checked=false;
            // Persist the guard BEFORE deletion so a failed local cleanup or a
            // later restart cannot silently recreate this account from old exports.
            if(!preferences.RetiredScopes.Contains(scope,StringComparer.Ordinal)){
                if(preferences.RetiredScopes.Count>=100)throw new InvalidDataException("Retired account limit");
                preferences=preferences with{RetiredScopes=[..preferences.RetiredScopes,scope]};
            }
            preferences=preferences with{Automatic=false};preferences.Save(config);
            await client.DeleteAccount(export,token,lifetime.Token);
            var cleaned=true;
            try{services.DeleteCredential(uri,export);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){cleaned=false;}
            try{BridgeFiles.WriteSnapshot(root,export.Actor,export.Client,uri,new Snapshot(1,services.Now().ToUnixTimeSeconds(),[],0,[]));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){cleaned=false;}
            session=new();
            SetStatus(cleaned?T("Serverkonto und eigene Serverinhalte gelöscht. Im Spiel /reload verwenden. Automatik pausiert.","Server account and authored server content deleted. Use /reload in game. Automatic sync paused."):T("Serverkonto gelöscht. Lokale Bereinigung unvollständig; Automatik bleibt pausiert. Im Addon Companion deaktivieren.","Server account deleted. Local cleanup incomplete; automatic sync stays paused. Disable Companion in the addon."));
            Log(T("Serverkonto gelöscht; automatische Neuerstellung gesperrt.","Server account deleted; automatic recreation blocked."));
        }
        catch(OperationCanceledException)when(closing){}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or CryptographicException or JsonException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception){
            if(!closing){SetStatus(T("Zugangsaktion nicht abgeschlossen. Datei, Passwort, Charakter und Serverzugang prüfen. Es wurden keine Zugangsschlüssel angezeigt.","Access action not completed. Check file, password, character and server access. No access keys were displayed."),true);Log(T("Zugangsaktion nicht abgeschlossen.","Access action not completed."));}
        }
        finally{busy=false;nextPoll=services.Now().AddMilliseconds(timer.Interval);if(!closing)SetAccessBusy(false);}
    }
}
