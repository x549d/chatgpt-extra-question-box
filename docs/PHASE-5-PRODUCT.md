# Phase 5 product engineering log

Baseline: `24dde22`. Existing 39 xUnit tests and 80 self-tests retained.

## 5A — local persistence

Windows inbox `winsqlite3.dll` is used directly, without a server or additional native runtime. Production path: `%LOCALAPPDATA%\ContextNotesNative\data\history.db`. Tables: `annotations` (account ownership, immutable quote, Thread ID, timestamps, JSON UI/draft snapshot, title/archive/delete flags), `messages` (unique ID, foreign key cascade, ordered sequence, role, verbatim text, timestamp, turn ID, completion and committed flags), `settings`. Schema version 2; transactional v1→v2 migration and pre-migration backup. Future schema versions and failed integrity checks are rejected without deleting files.

WAL + synchronous FULL; bounded busy timeout; serialized transactions; parameter binding; immutable queued snapshots every 750ms; explicit create/send/final/close flushes. On normal exit, SQLite online backup creates `history.db.backup.db`. Busy/disk failures retain pending snapshots for retry. An interrupted response restores as Failed/uncommitted, never auto-resends. New threads are durable; restored annotations must validate account and call `thread/resume`. An unavailable thread requires explicit new-context confirmation. SQLite backups must be restored with Companion stopped, preserving the current database and its WAL/SHM as a separate set first.

No tokens/cookies/pairing codes are collected or stored. Conversation content exists only in history storage and the official App Server's own durable session store. Local SQLite is not encrypted; Windows user profile access controls apply. Soft deletion does not remove SQLite free pages or copies in backups.

Protocol source: [official App Server documentation](https://learn.chatgpt.com/docs/app-server), checked 2026-11-18 (`thread/start`, `thread/resume`). SQLite durability: [SQLite pragma documentation](https://sqlite.org/pragma.html).

Validation: Release 0 warnings/errors; 39 original + 9 persistence tests (48 total). Tests cover 3 annotations × 3 turns, ordering, draft/UI/quote restoration, account isolation, interrupted output, transaction rollback, uniqueness, concurrent writes, version migration, future schema rejection, backup/reopen and exit flush. Real UI recovery and live Thread resumption are tracked separately in the final acceptance report; automated pipe tests are not live-account acceptance.

## 5B — history management

Open with Ctrl+Alt+H, the annotation's History button, or the selection entry context menu. Lightweight WPF history window supports one account scope at a time, 300ms debounced background search across quote/title/questions/answers, creation-date range, archive filter, 50-record pages, rename, archive/unarchive, soft-delete/restore, confirmed per-account purge and backup export. Opening a record rechecks the current official account; old scopes remain browsable but cannot continue under another account. No automatic resend. Annotation ID deduplication prevents duplicate restored windows. Deleted/archived active popups are closed and delayed saves suppressed so purging cannot resurrect them.

Validation: 55/55 xUnit (39 original, 9 persistence, 7 history); Release 0 warnings/errors. Real UI checks remain pending deployment.

## 5C — message rendering

Existing AnnotationWindow now renders headings, bold/italic, lists, blockquotes, tables, inline code, fenced code and HTTP(S) links. Unclosed fences show partial code and incomplete emphasis stays literal. Code uses a read-only monospace editor with horizontal scrolling, a language label, raw-source copy, and collapse for more than 12 lines. Long messages have expand/collapse; question/answer copy preserves source. No embedded HTML, remote images or executable URI schemes. Regeneration and individual message deletion are intentionally not offered: this release does not implement server history rollback, so local controls must not pretend to change the server context.

Validation: 64/64 xUnit, including 9 Markdown cases (Chinese/English, LF/CRLF fidelity, every prefix of a streamed response, long code WPF layout, table and safe links); Release 0 warnings/errors. Original Phase 3/4 tests remain unchanged.

## 5D — model and usage management

Model/usage window is available from annotations and history. Catalog is read through real `model/list` pagination; selections are validated against visible server models. Per-account default lives in `settings` and per-annotation choice in the snapshot; `turn/start.model` applies it. Busy/closed annotations, changed accounts and unavailable model names are explicitly rejected. `account/rateLimits/read` renders only returned percentage/window/reset/status fields; unsupported data displays unavailable. No cost or token consumption is invented. The installed Codex CLI generated its public JSON schema into a temporary workarea directory for contract verification (not shipped or committed).

Validation: 73/73 xUnit (9 new model/usage/resume cases), Release 0 warnings/errors. Existing fake server was extended to support explicitly configured thread/resume and unsupported-method errors; no baseline tests were removed. Live-account model/catalog/usage checks remain pending deployment.

## 5E — bounded work and fault recovery

Completed message views are reused; background UI refreshes coalesce into one queued Dispatcher callback. Terminal generation transitions are serialized against late streaming timers. SQLite snapshots update changed messages rather than deleting/reinserting the whole conversation. Cross-annotation message ID collisions roll back; cancellation tombstones are bounded. Failed App Server initialization disposes its transport. Persistence disposal is idempotent and late callbacks are ignored. Database write failures retain snapshots, show an unsaved-history notice and block an outgoing turn when critical input/thread state cannot be saved. Durable live threads resume after connection replacement; legacy ephemeral test threads retain their Phase 4 recovery behavior.

Validation: 84/84 xUnit and 80/80 self-tests. All 11 stability cases passed on the full run plus two additional repeats. Includes killed-process WAL transaction recovery, SQLITE_FULL injection using max_page_count, competing writer/retry, 5 × 10 concurrent generations/cancels, late-dispose calls, 1/5/10 WPF windows × 50 refreshes and 20 annotations / 1000 messages. First measured load run: 210ms elapsed, 406ms process CPU delta, +12,103,680 private bytes (test-host process, not isolated Companion telemetry). This is a reproducible bounded stress test, not an hours-long soak or Windows suspend certification.

Live official account validation: 10 server-advertised models; actual usage fields available; three synthetic annotations × three actual AI turns (18 messages), then a separate process reopened the database and resumed the exact same three Thread IDs for one more turn each (24 messages total). Unique threads = 3; foreign synthetic account query = 0. No automatic resend on restore. Commands `--phase5-live-prepare` / `--phase5-live-resume` are opt-in diagnostics using synthetic content; output is metadata only. Real desktop, Codex restart, suspend/resume and clean-machine installer checks remain separately tracked.

## 5F — packaging and installation

Evaluated MSIX (required package signing/certificate trust), WiX/MSI (more complex package/component authoring for this small per-user plugin), and Inno Setup. Chose per-user Inno Setup: no elevation, Windows version and actual .NET Desktop Runtime check, versioned payload, official Codex marketplace/plugin commands, SHA-256 verification, Hook manifest check, owned startup shortcut, watcher startup, migrations, attempted rollback and explicit history-retention choice at uninstall. Hook runtime trust and real selection UI still require desktop verification; installer does not bypass Codex approval.

`Build-Release.ps1` produces a setup EXE, portable ZIP, SHA-256 files and an immutable payload with a per-file hash manifest. `Manage-Installation.ps1` supports preflight, installation and uninstall; it does not rewrite the entire Codex configuration. `Test-Installation.ps1` uses the real local Codex CLI with isolated profile/data/startup directories. All 13 assertions passed, including reinstall, version update, forced failed update, pre-migration schema rollback, successful migration, history retention/removal and unrelated config preservation. These are local profile tests, not a clean Windows VM.

Inno Setup 6.7.4 was downloaded from its official release and its valid Pyrsys B.V. Authenticode signature verified. The compiler's normal installation encountered HKCU Uninstall key access denied; the officially supported `/PORTABLE=1` mode succeeded. This project's setup EXE was also actually executed: after correcting registry-only .NET detection to actual `dotnet --list-runtimes`, it reached installation but failed with exit 4 / RegCreateKeyEx code 5 on the host's HKCU Uninstall key, and rolled its extracted program files back. Therefore ordinary installer installation/uninstall on this managed host and clean-machine certification are **BLOCKED / NOT TESTED**, not PASS. The independent deployment manager and portable payload remain usable.

The manifest's existing Proprietary license label was preserved; LICENSE records the missing maintainer-confirmed grant instead of inventing a license. PRIVACY.md states actual implementation and explicitly remains a draft pending publisher/contact details. No public upload, formal tag or push was performed. Unsigned setup, legal completion, clean Windows acceptance and Windows suspend/wake remain public-release gates.

Sources checked: [Inno Setup per-user privileges](https://jrsoftware.org/is6help/topic_setup_privilegesrequired.htm), [MSIX signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview), [official Inno compiler portable mode](https://jrsoftware.org/inno-setup/documentation/topic_technotes.htm).
