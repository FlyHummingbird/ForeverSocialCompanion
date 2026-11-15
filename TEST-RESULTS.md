# Companion 1.2.1 verification — 2026-11-15

Builds completed with zero warnings and errors using the installed .NET 10 SDK.

| Suite | Result | Scope |
| --- | --- | --- |
| C# validation and synchronization | 37/37 | Parser, privacy, storage, HTTP download-only mode, upload deduplication, invalid snapshots, scope changes and retries |
| Windows component regressions | 94/94 | English/German UI, invitation parsing/copy/paste/file reuse/server binding, existing credentials, actual Forms timer, minimized operation, automatic preference and character restoration, delayed responses, overlap prevention and cancellation |
| Real API background integration | 6/6 | Actual ASP.NET API, two synthetic accounts, actual Forms timer, minimized and restarted windows, unchanged local exports, credentials excluded from files |
| Addon/API integration | 13/13 groups | Shipped Lua modules with simulated WoW for Retail/Forever and English/German; posts, comments, likes, reviews, awards, follows, listings, updates, withdrawals and privacy |

The background regression specifically reproduces the old failure: a second account adds a server post while the local account's SavedVariables remain byte-for-byte unchanged. The minimized app downloads it on an automatic tick. A subsequent server update is downloaded after restarting the window with the saved automatic preference.

Component tests run actual WinForms handlers with an injected HTTP handler and isolated files. They explicitly install a Forms synchronization context because they do not run the production `Application.Run` entry point. HTTP-library continuations do not depend on a UI context. The separate real-HTTP test uses the local API rather than that injected handler.

Reports are in `TestArtifacts/windows-ui-121-results.json`, `unit-121-results.json`, `background-121-results.json` and `e2e-121-results.json`. Source hashes are recorded in the background/integration reports. These are development evidence and are excluded from the portable runtime package.

The 24 new Windows checks cover DE/EN/raw invitations, Windows line endings, duplicates/oversize/UNC-path rejection, explicit-only clipboard access, no code in settings or logs, automatic file reuse on Connect, cross-server protection, clipboard failure recovery, file-picker cancellation, unavailable files and existing credentials. Clipboard operations and invitation pickers are injected so automated tests do not read or overwrite the user's real clipboard. The actual private invitation is separately parsed read-only with code output suppressed. No real enrollment or real player upload is performed by these checks.

The packaged entry point is checked separately for its STA attribute. Distribution files are checked against the generated SHA-256 manifest. The final ZIP is scanned with Microsoft Defender; the scan report is stored separately. A clean scan is not a code signature or a guarantee of SmartScreen reputation.

Not manually verified in a running WoW client: loading a downloaded snapshot with `/reload`, native Explorer folder selection, actual second-player gameplay and the rendered tray icon in the user's desktop session. No real player content was uploaded by these tests. No live backend deployment was needed for this client-only update.

Manual acceptance steps are included in the README. In-game import still requires `/reload`; the companion cannot acknowledge that WoW imported a file.

The integrated-cache tests additionally cover declared-slot detection before HTTP, restoring a cache overwritten by an addon update without another upload, verified backups, safe retirement of the old two-file addon, rejection of unknown files/executable legacy content and backup scope limits. Compact UI regressions cover hidden setup/history, first setup, independent expansion, one-click enrollment, missing invitations and multiple characters. No real game rendering or import is claimed.


The final single-file EXE was launched using its bundled .NET and Forms dependencies with an external test-only startup hook. It constructed a real window with isolated settings, checked first-run setup and the sync action, verified the STA entry point, then exited before reading personal settings or performing network operations. The hook is not distributed. Report: `TestArtifacts/single-file-startup-121.json`. The portable archive contains one executable and seven small help, license and integrity files, with no loose DLLs.
