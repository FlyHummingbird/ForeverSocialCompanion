# Forever Social Companion

Optional Windows app for the Forever Social WoW addon. You can keep using the
addon without this app.

## English

1. Extract the ZIP and open **ForeverSocialCompanion.exe**. Windows 10/11 x64;
   no separate .NET installation required.
2. In WoW, install Forever Social 1.4.0 or newer, open **/fs companion**, enable
   the optional connection and use **/reload** to save your export.
3. On first setup, choose your **_forever_** or **_retail_** folder.
   No invitation code or registration form is needed.
4. Select **Sync now**. Setup fields stay hidden after setup; **Change setup**
   opens them again. Existing credentials are reused.
5. Use **/reload** in WoW to load downloaded data. New or changed game content
   must also be saved by WoW before the app can read it.

Enable automatic synchronization to check the server every 30 seconds while the
app is running. **Keep running in background** leaves it in the notification
area. Closing the app stops synchronization. Automatic polling cannot make WoW
read updated files without a reload.

**Manage access** keeps optional account tools separate from setup. **Back up access**
creates a password-encrypted `.fsaccess` file outside the game folder (12+ character
password, entered twice). On another PC, permit the same character in the addon,
select the same server, then **Import access** and enter that password. The app
checks the account with the server before storing it in Windows Credential Manager.
Keep the password separately: possession of both grants account access. No new
invitation code is needed.

**Delete server account** requires confirmation and removes that account's authored
server content. It pauses automatics and prevents silent account recreation after
restart. Local game data and copies held by others remain. **Reconnect after
deletion** deliberately allows publishing your saved content again. Account tools
need API 1.2.0; basic sync remains compatible with older servers.

Only explicitly shared public content is exported. Private notes, encounters,
blocks and access credentials are excluded. A server account does not verify
ownership of a WoW character. See **PRIVACY.md** for details.

This download is not code-signed. Windows may display a warning for an unknown
publisher. Do not disable Windows security or add exclusions. Download only from
[the project's GitHub releases](https://github.com/FlyHummingbird/ForeverSocialCompanion/releases).

## Deutsch

1. ZIP entpacken und **ForeverSocialCompanion.exe** öffnen. Windows 10/11 x64;
   eine zusätzliche .NET-Installation ist nicht nötig.
2. In WoW Forever Social 1.4.0 oder neuer installieren, **/fs companion** öffnen,
   die optionale Verbindung aktivieren und mit **/reload** speichern.
3. Bei der ersten Einrichtung den Ordner **_forever_** oder **_retail_** auswählen.
   Ein Einladungscode oder Registrierungsformular ist nicht nötig.
4. **Jetzt abgleichen** wählen. Die Felder bleiben danach eingeklappt und lassen
   sich über **Einrichtung ändern** öffnen. Vorhandene Zugänge werden übernommen.
5. Heruntergeladene Daten im Spiel mit **/reload** laden. Neue oder geänderte
   Spielinhalte muss WoW ebenfalls speichern, bevor die App sie lesen kann.

Mit aktivierter Automatik wird der Server alle 30 Sekunden geprüft, solange die
App läuft. **Im Hintergrund weiterlaufen** lässt sie im Infobereich geöffnet.
Beenden stoppt den Abgleich. Die Automatik ersetzt den Reload im Spiel nicht.

**Zugang verwalten** enthält die freiwilligen Kontowerkzeuge. **Zugang sichern**
erstellt eine passwortverschlüsselte `.fsaccess`-Datei außerhalb des Spielordners
(mindestens 12 Zeichen, zweimal eingeben). Auf einem zweiten PC denselben Charakter
im Addon freigeben, denselben Server auswählen, **Zugang übernehmen** wählen und
das Passwort eingeben. Erst nach Prüfung am Server speichert die App den Zugang
im Windows-Anmeldeinformationsspeicher. Passwort getrennt aufbewahren: Datei und
Passwort zusammen erlauben Kontozugriff. Ein Einladungscode ist nicht nötig.

**Serverkonto löschen** fragt vorher nach und entfernt dieses Konto und dessen
eigene Serverinhalte. Die Automatik pausiert; eine unbemerkte Neuerstellung nach
Neustart ist gesperrt. Lokale Spieldaten und fremde Kopien bleiben erhalten.
**Nach Löschung erneut verbinden** erlaubt bewusst, gespeicherte Inhalte erneut
zu veröffentlichen. Die Kontowerkzeuge benötigen API 1.2.0; der normale Abgleich
bleibt mit älteren Servern kompatibel.

Übertragen werden nur ausdrücklich freigegebene öffentliche Inhalte. Private
Notizen, Begegnungen, Blocklisten und Zugangsdaten bleiben ausgeschlossen.
Ein Serverkonto bestätigt keinen Besitz eines WoW-Charakters. Einzelheiten
stehen in **PRIVACY.md**.

Die App ist noch nicht codesigniert. Windows kann einen unbekannten Herausgeber
melden. Windows-Schutzfunktionen bitte eingeschaltet lassen und keine Ausnahmen
anlegen. Downloads gibt es in den
[GitHub-Releases des Projekts](https://github.com/FlyHummingbird/ForeverSocialCompanion/releases).

The EXE includes its runtime. The small accompanying text files contain help,
privacy information and required licenses; there are no loose runtime DLLs.
