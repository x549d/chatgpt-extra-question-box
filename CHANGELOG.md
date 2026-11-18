# Changelog

## 0.3.0 — Phase 5 preview (2026-11-18)

- Local SQLite history, transactional migrations, online backups and interrupted-response recovery.
- Account-scoped search, dates, pagination, rename, archive, trash and confirmed deletion.
- Markdown and fenced code, raw copy, safe links and long-message folding.
- Server-provided model catalog, saved account defaults, individual model selection and actual limit snapshots.
- Coalesced UI updates, cached message views, bounded cancellation memory and storage-failure handling.
- Per-user Inno Setup packaging and transactional plugin deployment/uninstall with default history retention.

Release signing, publisher-confirmed legal material, clean-machine acceptance
and Windows sleep/wake testing are separate release gates; consult the final
Phase 5 acceptance report for actual test status.

### 5G–5J follow-up

- Restored the installed Companion and watcher after a verified SQLite backup.
- Corrected history mutation timestamps and native picker text contrast found during real desktop checks.
- Added migration-free SQLite online backup/restore for upgrades, and preserved recovery data if rollback fails.
- Refreshed verified files during same-version preview updates and protected isolated installer tests from production lifecycle signals.
- Added per-user registry preflight, optional Start Menu shortcuts, SHA256SUMS, release provenance and portable payload audit.
- Retained all baseline tests: 88 xUnit, 80 self-tests and 16 isolated installation assertions. Complete desktop and clean-machine installation remain separate release gates.
