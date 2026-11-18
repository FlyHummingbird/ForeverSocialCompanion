# Companion 1.4.0 / API 1.2.0 verification — 2026-11-18

Builds completed with zero warnings/errors under .NET 10. Tests use synthetic
accounts and isolated settings. No real player records were uploaded.

| Suite | Result |
| --- | --- |
| unit | 47/47 |
| windows-ui | 102/102 |
| enrollment | 15/15 |
| background | 6/6 |
| access | 6/6 |
| e2e | 13/13 |
| e2e-public | 13/13 |
| public-https | 9/9 |

Core tests cover parsing, validation, atomic storage, access scopes, consent,
existing claims, encrypted transfer, tampering, publication receipts, feed caps,
cache rewrites and revoked in-flight account objects. Windows tests exercise actual
English/German Forms handlers, timers, busy/cancel/retry behavior, account actions,
encrypted backup/import, deletion guards and safe file destinations.

Real local API tests check consent, authentication, account metadata, exact stored
receipts, conflicts, persistence and rate limits. Separate real HTTP + Forms runs
exercise background polling and access export/import/deletion across app instances.
Local and public integrations use shipped Lua modules under mocked WoW plus the
actual C# file bridge: profiles, posts, mentions, replies, likes, private drafts,
temporary mute, reviews, follows, groups and content lifecycle. Public HTTPS keeps
normal certificate validation and synthetic accounts are removed afterwards.

The actual single-file EXE was started with its bundled runtime and Forms library
through an external test-only startup hook. STA and a real first-run window were
checked without personal settings/network. The hook is not distributed. Portable
layout: one EXE plus seven small help, privacy, integrity and license files; no
loose DLLs. Its ZIP passed Microsoft Defender without changing security settings.
The executable remains unsigned; this is no guarantee against SmartScreen warnings.

API 1.2.0 runs on Unraid with the existing data, permissions, network and container
limits. A private backup and the stopped previous container remain for rollback.
Format 1 and existing credentials are preserved. Server accounts do not establish
Blizzard character ownership.

The addon separately passed 3,060 Lua checks, 68 simulated network scenarios and
15 release groups. Native WoW rendering/import, real two-player gameplay and
interactive Explorer/password dialogs remain manual checks. The bridge still
requires /reload. Screenshot attachments are not implemented.

Detailed private-development reports live in TestArtifacts and are excluded from
portable and source ZIPs. Cryptography and privacy details: PRIVACY.md.
