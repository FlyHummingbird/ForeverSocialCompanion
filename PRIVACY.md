# Data and permissions

The companion reads only `WTF/Account/<account>/SavedVariables/ForeverSocial.lua` in a game folder you select. WoW names SavedVariables files after the addon: this account-global file contains only `ForeverSocialBridge`. The full `ForeverSocialDB` is stored in a different, per-character `.../<realm>/<character>/SavedVariables/ForeverSocial.lua`, which the companion never reads. Sharing is off until you enable `/fs companion` for that character. The export contains the most recently saved character per WoW account, its client flavor, save time and its permitted public records. Public profile and public follow sharing require their existing separate consent. Posts, comments, likes, group listings and subjective reviews are intended for public sharing once you enable this connection.

Private notes, encounters, drafts, mutes, blocks, bookmarks, private follows, review history, account paths, credentials and chat logs are excluded from the export. The server receives your public character identifier, content and ordinary connection metadata. It keeps account token hashes and bounded public records, with 30-day content retention. Server accounts are unverified claims to names, not proof of WoW character ownership. Registration does not require an invitation. The app enrolls a character only after you explicitly click Sync now or Connect character and it has found a permitted addon export. This permission is a local consent signal, not remote proof of identity. Existing claimed names are not reassigned without their credential. Enrollment is limited to 20 requests per minute across this server; authenticated API requests retain their separate limits.

Access tokens are stored in Windows Credential Manager for your Windows user, never in the addon, generated data or settings JSON. Settings save the selected game folder, endpoint, public character identifier/client, automatic-update preference and bounded account-scope identifiers that prevent automatic recreation after deletion. Settings are limited to 16 KB. Obsolete invitation-file references from older versions are ignored and omitted when settings are next saved. Deleting a server account through the authenticated API revokes its token and removes its server records; other players may retain previous copies. Disabling the addon option clears local server-imported records and stops the next export. It cannot recall public copies. Pause automatic updates, use Exit in the tray menu, or close the window with X to stop the optional 30-second checks. Minimizing hides the window in the system tray and keeps opted-in checks running. The automatic preference resumes on the next manual launch; no Windows autostart, telemetry, automatic executable updates or background service is installed.

Automatic checks download server content even when no local content has changed. Unchanged local content is not uploaded again during the same session. The in-memory activity list is limited to 30 status messages and excludes credentials, exception response bodies and private content; it is not a persistent diagnostic log. WoW still needs `/reload` to load a downloaded snapshot. The app does not control the game or confirm an in-game import.

The only regular game-file write is `ForeverSocial/Data/Companion.lua`, a data slot declared by the main addon. No separate addon or TOC is created. They contain quoted, validated data generated locally, never downloaded executable Lua. These files are loaded on `/reload`; Restart WoW once after upgrading to the integrated addon version. The app needs write permission for that exact data directory; it does not elevate itself. Choose a writable installation or grant the appropriate folder permission separately if necessary.

The app never injects into WoW, reads game memory, installs drivers, captures the screen, sends gameplay keystrokes or changes antivirus/SmartScreen settings. It uses HTTPS for remote servers, preserves certificate validation and refuses automatic HTTP redirects with credentials. HTTP is permitted only on loopback for local tests. No statement here is a guarantee of antivirus classification or approval by Blizzard.

Legacy migration backs up the two recognized old data-addon files outside the game installation before removing them. It refuses links, unexpected files and backups inside the game folder. Old data is parsed as bounded tables and re-quoted; it is never executed. Backup files can contain previously downloaded public cache data, but no invitation or bearer tokens. SavedVariables are not modified by migration.

## Explicit account tools (1.4.0)

Only **Back up access** intentionally copies an existing credential to a file you
choose outside the game installation. The `.fsaccess` file encrypts endpoint,
client, character, community and bearer token with AES-256-GCM (random 16-byte salt,
12-byte nonce, 16-byte authentication tag; PBKDF2-HMAC-SHA256, fixed 600,000 rounds).
The password is entered twice on export, must contain 12–256 characters and is not
saved. Transfer input is capped at 16 KB; plaintext at 4 KB. Key/plaintext byte
buffers are cleared after use; managed strings can remain until garbage collected.
The file and password together grant the same account access on both PCs; this is
not a device-specific token or Blizzard verification. Keep them separately.

Import requires a matching permitted local addon export and exact server/client/
actor/community scope. The server validates the credential before Windows stores
it. Wrong passwords, altered files and mismatched scopes cannot replace access.
There is no automatic credential backup, clipboard copy or upload of this file.

**Delete server account** authenticates and confirms the selected scope. It removes
the account and its own server records, revokes access on every PC sharing that
token, pauses polling, clears the local imported data slot and removes the local
credential where possible. It retains local SavedVariables and others' copies.
A persisted guard blocks re-enrollment until explicit reconnection/import. If the
server request or local cleanup fails, the app reports incomplete work and keeps
the guard; retry deletion or disable the addon connection. Existing administrative backups may retain older data; this endpoint does not
purge them. Deletion is not claimed to erase independent backups or previously
shared public copies.

Post acknowledgements are a bounded extra list of the authenticated account's
stored posts. They represent the latest downloaded version, not worldwide delivery
or confirmed game import. Private draft/mute state is never part of this list.
