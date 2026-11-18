# Forever Social Companion

**An optional Windows companion for the Forever Social WoW addon.**

[**Download for Windows**](https://github.com/FlyHummingbird/ForeverSocialCompanion/releases) · [Setup / Einrichtung](Docs/USER-README.md) · [Privacy](PRIVACY.md)

## English

Keep your community connected through an optional server sync. The Companion
exchanges public posts, profiles and interactions that you explicitly permit in
the addon. Forever Social itself remains usable without the app.

- **One EXE.** No loose runtime DLLs and no separate .NET installation.
- **A compact overview.** Character, connection status and Sync now; setup opens only when needed.
- **Optional background polling.** Check the server every 30 seconds while the app is running.
- **One WoW addon.** Downloaded data lives inside Forever Social, without a second data addon.
- **Private means private.** Notes, encounters, drafts and mute/blocklists are excluded from the export.
- **Portable access.** Password-encrypted account backup/import and confirmed account deletion under Manage access.
- **Precise post status.** API 1.2.0 supplies acknowledgements of exact stored post versions.

Download the Windows ZIP under **Releases**, extract it and open
**ForeverSocialCompanion.exe**. You need Windows 10/11 x64 and Forever Social
1.4.0 or newer. Enable the connection with **/fs companion**, save with
**/reload**, select the game folder in the app and click **Sync now**.
No invitation code is needed. Existing access is reused.

**WoW still needs /reload** to save new exports and load downloaded data.
Background polling is not live HTTP inside the game. Server accounts do not
verify ownership of a WoW character.

The current download is **unsigned**. Windows can show an unknown-publisher
warning. No security exceptions or disabled protection are required.

## Deutsch

Die optionale Companion gleicht ausdrücklich freigegebene öffentliche Inhalte
mit dem Community-Server ab. Du kannst Forever Social weiterhin ohne die App
nutzen.

- **Eine EXE:** keine losen Laufzeit-DLLs und keine zusätzliche .NET-Installation.
- **Kompakte Übersicht:** Charakter, Status und Jetzt abgleichen; Einrichtung nur bei Bedarf.
- **Optionale Automatik:** prüft den Server alle 30 Sekunden, solange die App läuft.
- **Ein WoW-Addon:** Serverdaten liegen direkt im Hauptaddon.
- **Private Daten bleiben privat:** Notizen, Begegnungen, Entwürfe, Stumm- und Blocklisten werden nicht exportiert.
- **Zugang mitnehmen:** passwortgeschützte Sicherung/Übernahme und bestätigtes Löschen unter Zugang verwalten.
- **Genauer Beitragsstatus:** API 1.2.0 bestätigt den konkret gespeicherten Beitragsstand.

Unter **Releases** das Windows-ZIP laden, entpacken und
**ForeverSocialCompanion.exe** öffnen. Benötigt werden Windows 10/11 x64 und
Forever Social ab 1.4.0. Im Spiel **/fs companion** öffnen, die Verbindung
freigeben und mit **/reload** speichern. In der App den Spielordner auswählen
und **Jetzt abgleichen** anklicken. Ein Einladungscode ist nicht nötig.
Bestehende Zugänge werden übernommen.

**/reload bleibt erforderlich**, damit WoW neue Exporte speichert und
heruntergeladene Daten lädt. Die Automatik macht daraus keine Live-Verbindung
innerhalb des Spiels. Ein Serverkonto bestätigt keinen Charakterbesitz.

Der Download ist derzeit **nicht codesigniert**. Windows kann einen unbekannten
Herausgeber melden. Schutzfunktionen müssen dafür nicht deaktiviert werden.

## Source and verification

Code: [MIT](LICENSE). Runtime license notices accompany the Windows download.
[Development and backend setup](Docs/DEVELOPMENT.md) ·
[Verification scope](TEST-RESULTS.md). Local regression tests do not establish
compatibility with every running WoW client.
