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
