# Forever Social Companion 1.2.1

Optional Windows companion for Forever Social **1.3.2 or newer**. The portable x64 package includes the .NET runtime inside one EXE, with no loose runtime DLLs. Required notices and help are included as text files. It synchronizes the addon's explicitly permitted public data with a separate backend; the addon itself performs no HTTP requests. English and German Windows interface text is selected from the operating system language.

**Current status:** functional test build. Windows binaries are not code-signed. SmartScreen may warn about a new, unknown application; Defender classification and SmartScreen reputation are different checks. A successful local scan cannot guarantee that every Windows installation will accept a download. Do not disable protection or add exclusions for this app. A public release should be built with a trusted code-signing certificate and reviewed for false positives through Microsoft's submission process if needed.

[Download for Windows](https://github.com/FlyHummingbird/ForeverSocialCompanion/releases) · [Player instructions](Docs/USER-README.md)

## Setup

1. Install Forever Social 1.3.2 or newer and restart WoW once after upgrading. Open `/fs companion` and explicitly allow export/server data for this character. Public profile and public follows remain separate options. This command opens the setup window even when the main addon window is closed.
2. Use `/reload` to save the export. Start `ForeverSocialCompanion.exe`, select your `_forever_` or `_retail_` folder, then select the exported character. You can also enter or paste the full folder path and press Enter. If the Windows folder dialog fails, the app stays open and offers this alternative.
3. Use the administrator's HTTPS server address. Paste the invitation with **Paste**, or select its private text file once with **Load invitation**. A selected invitation file is remembered for that server on this PC; **Connect character** then reads its code automatically for additional characters. An already connected character uses its existing credential without enrolling again. Access tokens stay in Windows Credential Manager. An existing claimed name on another PC needs the original credential or administrator-assisted account recovery; this is not a Blizzard identity verification system.
4. Click Synchronize. The downloaded cache is stored inside the main addon. `/reload` imports downloaded data and writes new exports. Enable **Synchronize automatically every 30 seconds** to keep downloading server updates while the app is open, including when your own export has not changed. Use `/reload` in game to load the downloaded snapshot. The normal view shows only the character, sync status and actions. Folder, server and invitations are under **Change setup**; recent activity expands separately. A single known character needs no selector.

After saving with `/reload`, the next automatic check discovers newly saved exports. The character list also refreshes when you return to the app and before each connection/synchronization action. Missing exports show setup/reload instructions; an empty invitation field is explained before enrollment. Returning to the app alone performs no HTTP request.

## One addon folder

Use Companion 1.2.0 with addon 1.3.2 or newer. The app writes only `Interface/AddOns/ForeverSocial/Data/Companion.lua`; the main addon declares and loads that data slot. It does not create another addon or modify TOCs. Install the addon update first, then restart WoW once. Subsequent data updates use `/reload`.

Old `ForeverSocial_Companion` files are backed up outside the game folder and removed after the integrated cache is ready. Automatic cleanup accepts only the two recognized generated files and refuses directories containing extra files or links. Backups are under the companion settings folder's `Backups` directory. The data-only migration parser never executes old Lua. Existing account and character SavedVariables remain untouched. Keep using the new companion after migration; older companion versions still create the retired folder.

Addon managers may replace the generated cache during an update. The next successful companion poll detects the changed file and restores the current snapshot even if server content and local exports are unchanged. The addon still works locally while the companion is closed. No server account recovery or backend upgrade is required.

## Automatic updates and background operation

- Each automatic check fetches the current server snapshot. Unchanged local content skips only the upload, never the download. A changed save timestamp alone does not upload the same records again.
- The automatic-update checkbox, game folder, server and selected character are saved. The automatic preference resumes the next time you launch the app. Nothing is added to Windows startup.
- Minimize or click **Keep running in background** to hide the window in the Windows system tray. Double-click its icon to reopen it. Its menu can synchronize, pause/resume automatic checks or exit. **X closes the process** and stops synchronization.
- The overview shows the last successful sync, the export time and whether an updated data file is ready. This is a file-delivery status; the app cannot confirm that WoW has imported it. The last 30 session activities are shown without access tokens or server response bodies.
- Network and server errors preserve pending work and delay retries, up to five minutes. A manual sync remains available. Overlapping requests are prevented; exiting cancels an outstanding request.
- Unchanged snapshots are not rewritten on every poll. Missing payloads are recreated and unchanged files are refreshed at least once a day to keep their fetch time valid.

Version 1.1.0 fixes the previous 1.0.2 behavior that skipped the entire server request when the local export was unchanged. It also persists the automatic preference; older settings containing only a folder and endpoint remain compatible. Settings contain no invitation code or bearer credential.

This is a snapshot bridge like TSM's file-based updates, not live chat. Files are not reloaded continuously by WoW. The app must have write access to the main addon's Data directory; it requests no elevation. Use one companion instance per game installation and select the character you will play next. The generated snapshot inside the main addon is deliberately bound to one client and character, so switching characters requires another synchronization.

The backend is running on Unraid at `https://social.wgfreunde.com`, behind its HTTPS Cloudflare tunnel. Enrollment is invitation-only. The Windows package remains an unsigned test build; it is not a signed public release or a GitHub upload. [Privacy and permissions](PRIVACY.md).

## Easier invitations

The invitation controls provide **Load invitation / Change invitation**, **Paste** and **Copy**. A remembered private file is read only when you connect a new character or explicitly copy the code; simply opening the app or running automatic synchronization does not read that file or the clipboard. File selection reads and validates the invitation immediately. Supported files contain either the raw code or one `Einladungscode:` / `Invitation code:` line, including normal Windows line endings.

Only the selected file path and its server address are saved in settings. The invitation code is not copied into settings, game files, logs or distribution packages. Changing the server clears the code field and prevents automatic use of the previous server's invitation. Clipboard access is performed only by the explicit Paste and Copy buttons; a clipboard lock produces a recoverable message. After a successful connection the input field is cleared. If a remembered file was moved, use **Change invitation** to select it again.

There is no invitation code embedded in the public addon. The in-game companion checkbox grants export permission; Windows handles invitation setup. Each recipient still needs an invitation from the administrator once. Personal file references are kept in the user's settings, outside the portable package.

## Build and verification

Use the .NET 10 SDK. There are no third-party NuGet dependencies; only Microsoft's framework/runtime packages are used.

```powershell
dotnet build Tests/Tests.csproj -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/Tests.csproj --no-build
dotnet run --project WindowsTests/WindowsTests.csproj -- --report TestArtifacts/windows-ui-results.json
python Tests/background_e2e.py
./Build-Release.ps1 -AllowUnsignedTest
```

`Tests/end_to_end.py` uses the actual C# parser/HTTP client and shipped addon Lua modules with a mock WoW environment, across Retail/Forever and English/German. Python and Lupa are development-only dependencies. It exchanges posts, comments and likes, checks Unicode and verifies that private notes and foreign-author writes do not leak through. The public test uses the same honest application User-Agent as the C# client; Cloudflare may reject generic Python-urllib requests. No Cloudflare protection was disabled. No real game-client test has been performed.

For this integration test, place the complete `ForeverSocial` addon source (including `Tests/mock_wow.lua`) beside the `ForeverSocialCompanion` source folder, build `Tests/Tests.csproj`, install the development-only `lupa==2.8`, then run `python Tests/end_to_end.py`. The user addon ZIP intentionally omits those fixtures. The C# validation suite and GitHub workflow work independently of this sibling source. Integration coverage also includes reviews, awards, public follows, group listings, updates and withdrawals.

Windows component regressions exercise the actual WinForms click and keyboard handlers with injected dialog outcomes, including cancellation, shell failures, repeat selection, corrupt exports and revoked consent. They also run the actual Forms timer while minimized, check persisted settings across window restarts, download-only polls, retry backoff and request cancellation. `Tests/background_e2e.py` starts the real local API and uses two synthetic accounts to prove that minimized polling and restarting the app fetch new remote data without changes to the local SavedVariables. These tests do not automate the native Explorer dialog or a WoW game client. A separate check reads the actual packaged entry point and verifies its STA attribute.

Manual acceptance: connect a character, enable automatic updates and minimize. Have a second user publish and synchronize a post. Check that the app records a later successful sync and writes the new post to its data file; `/reload` should then show it in game. Close and reopen the app and verify that automatic updates and the selected character are restored. Repeat while briefly disconnected from the network, then restore connectivity. Companion 1.2.0 requires addon 1.3.2 or newer. Existing server credentials remain valid; no backend deployment is needed.

For a signed public package, import a trusted code-signing certificate into your user certificate store and run `Build-Release.ps1 -CertificateThumbprint <thumbprint>`. Without a certificate the script refuses a public build; test builds have `UNSIGNED-TEST` in their archive name and window title. Signing must be performed by the real publisher, with a trusted chain and timestamp. It establishes publisher identity, not a guarantee of SmartScreen reputation. The GitHub workflow produces explicitly unsigned test artifacts only and never publishes a release automatically. Create the repository from these source files, excluding `.env`, data, artifacts, certificates and local build caches as listed in `.gitignore`.

## Backend on Unraid

`Server` is an ASP.NET Core API. It runs as UID 1654 with a read-only container filesystem, a persistent `/data` mount, no extra capabilities and bounded request/record/storage limits. The initial durable store is an atomic, bounded JSON database guarded by a single-instance lock. It is not a MariaDB integration or a multi-node database; a future database provider can replace `CommunityStore` behind the same HTTP contract.

Set a random `FS_INVITE` of at least 24 characters in an untracked `.env`; keep it private. Create `data` owned by UID 1654 before `docker compose up -d --build`. Expose container port 8080 through your trusted HTTPS reverse proxy/tunnel. Never expose an unencrypted bearer-authenticated endpoint to the Internet. The supplied compose uses a dedicated `foreversocial_nw` bridge network; attach the tunnel connector to that network when configuring its route.

API: `GET /health`, `POST /v1/enroll`, authenticated `POST /v1/records`, `GET /v1/snapshot`, `DELETE /v1/account`. Records are author-bound to the enrolled account, filtered by client flavor and supported Forever community, validated and versioned; deleted posts/comments become terminal text-free tombstones. A backend account does not prove WoW identity or independently verify a shared encounter. Cached server data cannot overwrite existing directly received or local addon records and is never relayed through the P2P network.

Before changing or upgrading storage, stop the container and back up the complete `data` directory and `.env` securely. Do not copy live credentials into a public repository. The database is single-writer; run only one API instance per data directory.

Code: MIT. Included .NET runtime libraries retain their Microsoft license and third-party notices.
