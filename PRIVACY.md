# Privacy information — release draft

This describes the implementation, not a finalized public legal policy.
The publisher's legal identity and privacy contact must be supplied and
approved before public distribution.

Context Notes reads the selected text and its screen bounds from the active
Codex Chat window using Windows UI Automation. A question sends the selected
quote, the question and, when creating an explicitly recovered context, prior
completed local messages to the official Codex App Server. OpenAI service and
account policies govern that communication. This application does not read
browser cookies or process tokens, implement OAuth, or store authentication
credentials. It uses the account already available to the official server.

Quotes, questions, answers, drafts, model choice, account fingerprint, thread
IDs and window state are stored in the current Windows user's local
`%USERPROFILE%\.context-notes-native\data` SQLite database. The database and its
backups contain conversation content and are not encrypted by this app.
Windows user-profile permissions and any configured device encryption apply.
Legacy AppData and packaged LocalCache databases are retained separately after
an explicit migration; they are not silently deleted or overwritten. Different
views must be verified before migration, and conflicting records require review.
Ordinary diagnostic logs contain operational metadata, not full conversation
text. This project implements no separate analytics or history upload service.

Durable App Server threads also have their own Codex-managed local session
storage. Deleting a Context Notes record does not delete that independent
Codex history. Soft deletion retains the local record until the user confirms
permanent deletion. SQLite free pages and pre-existing backup files may retain
previous bytes. Exported backups must be managed separately by the user.

Uninstall keeps local history by default. An interactive uninstall offers
explicit removal of the known database/WAL/SHM and application backup files.
Silent uninstall always preserves history. No third-party account or Codex
authentication files are deleted. Installation rollback backups contain local
history when present and stay under the current user's ContextNotesNative
backups directory. They are removed after successful installation or recovery;
if recovery itself fails, the backup is retained and its location is reported.
Retained recovery backups are not silently removed by uninstall.
