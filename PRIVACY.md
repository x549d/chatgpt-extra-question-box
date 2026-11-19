# Privacy Policy & Data Handling

Last updated: 2026-11-19

- **Publisher**: `x549d`
- **Public Privacy Contact**: <https://github.com/x549d/context-notes-native/issues>

This document accurately describes how **Context Notes Native** handles user selections, questions, AI responses, local history, backups, and diagnostic logs.

## 1. Selection Reading & AI Cloud Processing

Context Notes Native reads the currently selected text and its screen coordinates from the active Windows Codex Chat window using Windows UI Automation (UIA). It does not inject into Codex processes, modify Codex files, or use the system clipboard to capture selections.

When you submit a question in an annotation window, the application sends:

- The quoted selection text
- Your question
- Prior completed local Q&A turns for that annotation (when starting a new thread or explicitly recovering an unavailable thread)

to the locally installed official **Codex App Server** (`codex.exe app-server`) over stdio JSON-RPC.

**Important**: The local Codex App Server is a bridge process, **not** a guarantee of offline AI processing. Submitted quotes, questions, and recovered contexts enter the corresponding AI model service workflow and are transmitted to OpenAI's cloud services according to your active Codex account. Provider-side data retention, model training settings, and workspace policies are governed by OpenAI's applicable terms and privacy policy (<https://openai.com/policies/privacy-policy/>). Context Notes Native does not independently verify or override provider-side policies and makes **no offline-AI, zero-retention, or no-training claim**. The read-only sandbox settings applied to App Server turns restrict generated tool actions and do not prevent cloud model inference.

## 2. Authentication & Credentials

Context Notes Native reuses the authenticated session already available to your official Codex installation. It does **not**:

- Read browser cookies
- Extract tokens from process memory
- Implement custom OAuth flows
- Collect passwords or store authentication credentials

Account separation for local history uses an irreversible SHA-256 scope fingerprint computed in memory.

## 3. Local Storage (SQLite)

Quoted selections, questions, assistant answers, unsent drafts, selected model names, account scope fingerprints, thread IDs, and window placement state are stored locally under the current Windows user profile:

- Database: `%USERPROFILE%\.context-notes-native\data\history.db` (plus SQLite `-wal` and `-shm` files)
- Backups: `%USERPROFILE%\.context-notes-native\backups\`
- Diagnostic logs: `%USERPROFILE%\.context-notes-native\logs\`

The SQLite database and its backups contain your conversation content and are **not encrypted at the application layer**. Protection relies on Windows user-profile permissions and any OS/device encryption (such as BitLocker) you have enabled.

Legacy AppData or packaged LocalCache databases from earlier preview builds are preserved separately after explicit migration and are never silently overwritten or deleted.

## 4. Diagnostic Logs & Telemetry

- **No Telemetry**: Context Notes Native implements **no telemetry, analytics, crash-upload, or remote history-sync service**. It contains no `HttpClient`, `WebRequest`, or custom remote API calls.
- **Local Logs**: Operational logs under `%USERPROFILE%\.context-notes-native\logs\` record only metadata (window states, UIA roles, rectangle counts/coordinates, text lengths, timings, and error categories). They do not record full quoted text, user questions, or AI answers.

## 5. Backups, Deletion & Data Retention

- **Soft Deletion**: Deleting an annotation moves it to Trash while keeping the local record until you confirm permanent deletion.
- **Permanent Deletion**: Confirmed permanent deletion removes the selected account's trashed annotations and cascading messages from the active SQLite database. However:
  - SQLite free pages and previously created local backup files may still contain older bytes until overwritten or manually removed.
  - Restoring an older backup created before deletion will restore records contained in that backup.
  - Durable Codex App Server threads also maintain their own Codex-managed session storage; deleting a local Context Notes record does **not** automatically delete Codex's own session files or provider-side cloud history.
- **Uninstall Retention Policy**:
  - Standard and portable uninstall **keep local history by default**.
  - Interactive standard uninstall explicitly asks whether to keep or remove known active database/WAL/SHM and application backup files.
  - Silent uninstall always preserves local history unless explicitly overridden via `/REMOVEHISTORY=1`.
  - Uninstalling never deletes Codex authentication files or other Codex plugins.
  - Temporary upgrade rollback backups under `%USERPROFILE%\.context-notes-native\backups\install-recovery-*` are removed after successful install/recovery; if recovery fails, the backup is preserved for manual inspection and is not silently deleted by uninstall.
