# Changelog

## 0.3.0 — Phase 6 preview validation (2026-11-19)

- Verified three new independent annotation threads and real full-client cold restart with continuation on the original thread.
- Fixed watcher recovery after a companion exits while the same verified Codex client remains foreground, retaining retry backoff and single-instance behavior.
- Separated CLI diagnostics from JSON output and validated the packaged deployment manager under PowerShell 7 and Windows PowerShell 5.1.
- Isolated TESTROOT uninstall identities from the production AppId; retained per-user privileges and fail-before-deployment registry preflight.
- Added runtime observation, four actual permission probes, legacy-host installation fixtures and third-party license inventory.
- Unified desktop and packaged history under the user's .context-notes-native directory; blocked silent empty replacements and added explicit backup import into new destinations.
- Preserved all existing tests: 102 xUnit, 80 self-tests and 19 installation assertions; release audits now also check staging files, runtime signature state and source revision existence.

Standard EXE installation, clean-Windows acceptance, complete UI stress coverage,
trusted signing and publisher-approved distribution terms remain release gates.

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
