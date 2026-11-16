# Companion 1.3.0 verification — 2026-11-16

Builds completed with zero warnings and errors using .NET 10 SDK.

| Suite | Result | Scope |
| --- | --- | --- |
| C# core | 40/40 | Parsing, validation, storage, invitation-free payloads, disabled-export refusal, existing claims, scoped sync, cache/migration |
| Windows components | 80/80 | Actual English/German Forms handlers and timers, first connection without a code, obsolete settings, existing access, no background enrollment, late exports, conflicts, minimized polling and retry/cancel behavior |
| Real local enrollment API | 11/11 | Consent/scope validation, open enrollment, conflicts, bearer protection, unchanged format-1 data, restart, 429 throttling and authenticated deletion |
| Real API background integration | 6/6 | Two synthetic accounts and actual minimized Forms timer; remote updates with unchanged local exports and restart |
| Local addon/API integration | 13/13 groups | Shipped Lua modules under mocked WoW, both clients and locales, public content lifecycle and privacy |
| Public HTTPS enrollment | 7/7 | API 1.1.0, no invitation, protected operations, duplicate refusal, synthetic upload/download and cleanup |
| Public HTTPS addon/API integration | 13/13 groups | Same complete content lifecycle through the deployed API with synthetic actors, cleaned up afterwards |

The actual single-file EXE was started with its bundled runtime and Forms library using an external test-only startup hook. It checked the STA entry point and constructed a real window with isolated settings; no personal settings or network were used. This hook is not distributed. The portable package contains one EXE and seven small help, privacy, integrity and license files, without loose DLLs.

The runtime archive is checked against its SHA-256 manifest and scanned with Microsoft Defender. A clean scan is not a signature or a guarantee against SmartScreen warnings. The current EXE is unsigned.

API 1.1.0 is deployed on Unraid with the original data volume, network and container restrictions. A private backup and the stopped previous container are retained for rollback. Existing accounts remain valid. Registration is open but does not verify WoW character ownership. No real player data was uploaded by these tests.

Reports are under TestArtifacts (development evidence, excluded from portable/source packages). The addon separately passed 2,967 Lua checks, 68 simulated network scenarios and 15 packaging groups. Real WoW rendering/import, native Explorer folder selection, and two-player gameplay remain manual checks. In-game import/export still requires /reload.
