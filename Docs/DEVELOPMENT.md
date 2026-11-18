# Development and deployment — Companion 1.4.0 / API 1.2.0

## Setup

Install Forever Social 1.3.2 or newer, open `/fs companion`, enable the optional connection and save with `/reload`. Choose `_forever_` or `_retail_` in the app and click **Sync now**. The default HTTPS endpoint is `https://social.wgfreunde.com`. No invitation, local code file or clipboard operation is required. The first explicit sync creates a server account; background polls and window activation never enroll a new character. Existing tokens remain valid and are reused.

The export is editable local data. It signals consent but cannot prove ownership of a WoW character. Registration reserves a name on a first-claim basis, without Blizzard verification. An existing claim returns HTTP 409 and cannot be overwritten by registering again. A lost credential requires administrator-assisted recovery after a separate ownership assessment; no automatic recovery or Blizzard login is implemented. Tokens stay in Windows Credential Manager. Old invitation-file settings are ignored and disappear on the next settings save.

## File bridge and background behavior

The app reads only bounded account-global `ForeverSocialBridge` exports. WoW writes them at `/reload` or logout; in-memory edits are unavailable before then. Only permitted public records are exported. Private drafts, mutes, notes, encounters, blocks, bookmarks and private follow/review history are excluded.

The main addon's TOC must declare `## X-ForeverSocial-Companion: 1` and `Data/Companion.lua`. This slot is validated before any enrollment or synchronization. Downloaded records are validated, quoted as data, and written atomically inside the main addon; remote Lua is never accepted or executed. A recognized old two-file data addon is backed up outside the game installation before migration. Unknown files and links are refused. SavedVariables are never written by the app.

Opted-in polling checks the server every 30 seconds while the app is running, including while minimized to the tray. Unchanged local records are not uploaded again, but new server data is still downloaded. Missing or replaced cache files are repaired and unchanged cache timestamps refreshed daily. Requests never overlap; failures back off to five minutes. Closing cancels active work and stops polling. Automatic settings persist across manual restarts; no Windows service or autostart is installed.

WoW needs `/reload` to import new data. One snapshot is bound to one selected client/character. Switching characters requires another sync. The app requests no administrator elevation and needs write permission to the addon's Data directory.

## Build and test

Use .NET 10 SDK. No third-party NuGet dependencies are required.

```powershell
dotnet build Tests/Tests.csproj -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/Tests.csproj --no-build
dotnet run --project WindowsTests/WindowsTests.csproj -- --report TestArtifacts/windows-ui-results.json
python Tests/enrollment_e2e.py
python Tests/background_e2e.py
python Tests/access_e2e.py
./Build-Release.ps1 -AllowUnsignedTest
```

The core suite tests validation, storage, parsers, consent, enrollment payloads and protected existing claims. Windows component tests run real Forms handlers/timers in English and German, with synthetic exports and injected HTTP. `enrollment_e2e.py` runs the actual API and covers open enrollment, consent metadata, conflicts, bearer authorization, persisted access and enrollment throttling. `background_e2e.py` checks a real API plus the minimized Forms timer. Tests never register real players.

`Tests/end_to_end.py` additionally uses shipped addon Lua modules under a mock WoW host across Retail/Forever and English/German, exchanging profiles, posts, comments, likes, ratings, awards, follows and groups. Place the complete `ForeverSocial` source beside the Companion source, build the test projects and install development-only Lupa 2.8. These tests do not establish behavior in a running WoW client or exercise the native Explorer folder picker.

Manual acceptance: enable export, reload, connect without entering a code, enable automatic polling and minimize. Have another user publish/sync. Check the later successful sync, then reload WoW to see their post. Reopen the app and confirm the selected character and automatic preference remain. Repeat with a temporary connection failure. A second PC cannot claim the same existing name without the original credential.

The portable package contains one self-contained EXE plus help, privacy, license notices and a manifest. A test-only startup hook verifies the actual bundled executable without touching personal settings or the network; it is not distributed. The app is unsigned until the publisher supplies a trusted code-signing certificate. `Build-Release.ps1 -CertificateThumbprint <thumbprint>` supports signing; unsigned packages remain explicitly marked. Do not disable Windows protections.

## Unraid backend

API 1.1.0 removes the invitation gate. Deploy this API before using Companion 1.3.0 for first-time registration. Existing 1.2.x credentials continue to sync; older apps need updating for new enrollments. Database format 1 is unchanged. No `FS_INVITE`, `.env`, DNS or tunnel change is required.

Create a persistent `data` directory owned by UID 1654 before `docker compose up -d --build`. Expose container port 8080 only through a trusted HTTPS proxy/tunnel. The dedicated `foreversocial_nw` network, read-only container filesystem, dropped capabilities, no-new-privileges, memory/PID limits and single-writer database remain in place. Back up data while the container is stopped before an upgrade; retain the previous image for rollback.

API: `GET /health`, `POST /v1/enroll`, bearer-authenticated `POST /v1/records`, `GET /v1/snapshot`, `GET /v1/account`, `DELETE /v1/account`. Enrollment requires `{format:1, enabled:true, client, actor, community}`. These client assertions are not identity verification. No token is returned for an already claimed name. Other operations still require the issued 256-bit bearer token; the server stores only its hash and binds writes to its account/author. Records are scoped by client and supported Forever community. Counts are bounded (1,000 accounts, 10,000 records, 1,500 records/account), with 30-day content retention.

Enrollment has a shared fixed-window budget of 20 attempts/minute (including invalid requests); other API operations retain 120 requests/minute per authenticated account or connection address. No untrusted forwarded-IP header is used. This is basic flood mitigation, not proof of personhood or central moderation. CommunityStore currently uses an atomic bounded JSON database and a process lock, not MariaDB or multiple replicas.

Code: MIT. Runtime libraries retain Microsoft's licenses and third-party notices.

## API 1.2.0 and Companion 1.4.0

Database format 1 and existing bearer tokens are preserved. `GET /v1/account`
returns only the authenticated account's id/client/actor/community. Both inspection
and deletion require the existing bearer and request rate limit. Store operations
recheck account existence under the storage lock to stop an already-authenticated
in-flight upload from resurrecting records after deletion.

Snapshots optionally include `published`, up to 500 actual account-owned post wire
records independent of the capped community list. It is not an assertion supplied
by an upload request. The client validates ownership, type, revisions and duplicate
IDs before any write. Both public content and acknowledgement digests control cache
rewrites. Old consumers ignore this extra field; missing fields give no receipt.

`AccessTransfer` uses .NET AES-GCM with a specified 16-byte tag and static
`Rfc2898DeriveBytes.Pbkdf2` (SHA-256, 600,000 rounds, 32-byte key). It has bounded
format/KDF parameters, random salt/nonce and fixed associated format data. It never
executes imported content. Password work runs outside the Forms thread; credential
and destructive actions share the synchronization busy guard. Native password,
file and deletion dialogs are owned by the main STA window; test seams are only
constructor-injected and no production CLI credential overrides exist.

Sources checked: [AES-GCM constructor](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.-ctor?view=net-10.0)
and [.NET 10 PBKDF2 guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0060).
The encrypted file grants shared account access, not separate per-device revocation
or a Blizzard identity claim. See PRIVACY.md and USER-README.md.
