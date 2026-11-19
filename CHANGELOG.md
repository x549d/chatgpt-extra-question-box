# Changelog

## 0.3.0 — Phase 7 release readiness (2026-11-19)

- Validated permanent deletion against the published runtime in a new synthetic database: 23 assertions covering account isolation, cascades, injected transaction rollback, WAL snapshots/checkpoints, fresh connections, and pre/post-deletion backup restores. Production code and user history were not modified.
- Clarified AI cloud processing through the local Codex App Server, publisher/contact placeholders, and the limits of deletion when older backups remain. Kept Proprietary terms unchanged and recorded maintainer ownership, distribution, privacy and branding decisions.
- Corrected environment inventory: VMware exists at a custom path. A separate full clone reached the Windows lock screen, but did not complete clean-OS acceptance. Cloud Windows evaluation has a user-approved RMB 20 total cap; trial eligibility is being checked and no cloud resource has been purchased.
- Clean Windows install/upgrade/uninstall, strict three-window simultaneous streaming, legal distribution decisions and trusted signing remain open gates. Reused Phase 6 stability and installer evidence without rerunning 30/60-minute sampling or installer suites.

## 0.3.0 — Phase 6 final closure & Release Candidate prep (2026-11-19)

- Root-caused standard installer Temp/HKCU Error 5 to inherited Windows `Low Mandatory Level` SACL on sandboxed build directories; added Temp preflight, Low-IL diagnostics/warnings, `-OutputDir`, and integrity-level probes without requiring admin privileges or modifying system ACLs.
- Fixed real installer defects: automatic creation of missing `CODEX_HOME` during fresh install, non-zero exit code (`GetCustomSetupExitCode`) and automatic fresh-install rollback (`/ROLLBACK=1`) on deployment failure, exact 16-hex isolated test AppId matching, isolated rollback backup directories, watcher readiness polling, and explicit `/REMOVEHISTORY=1` uninstall test flag (default always preserves history).
- Verified 29/29 standard EXE end-to-end install/reinstall/v1→v2 upgrade/failure-rollback/keep-history-uninstall/remove-history-uninstall assertions (`Test-StandardInstaller.ps1`), alongside 106/106 xUnit, 80/80 self-tests, 19/19 portable installation assertions, 6/6 permission probes, 9/9 hook diagnostics, and 12/12 release audit checks.
- Completed final-build Codex Desktop cold start (0 overlapping old PIDs, Watcher auto-launch, SessionStart hook single-instance dedup, real AI streaming Q&A persisted to SQLite with 0 baseline diff) and real window-move coordinate tracking.
- Completed security, privacy, third-party notice, and OpenAI/Codex trademark boundary audit (0 production NuGet packages, 0 telemetry/HTTP/cookie/token reads, 0 gitleaks findings across full git history and payload).
- 60-minute soak cancelled (`OPTIONAL / NOT REQUIRED / NOT RUN`); 30-minute continuous stability acceptance recorded in `docs/PHASE-6-ACCEPTANCE.md`.

Clean Windows OS (Sandbox/VM), trusted Authenticode signing, maintainer-confirmed public distribution license (`LICENSE`), and publisher privacy contact (`PRIVACY.md`) remain formal public release gates.

## 0.3.0 — Phase 6 preview validation (2026-11-19)

- Verified three new independent annotation threads and real full-client cold restart with continuation on the original thread.
- Fixed watcher recovery after a companion exits while the same verified Codex client remains foreground, retaining retry backoff and single-instance behavior.
- Separated CLI diagnostics from JSON output and validated the packaged deployment manager under PowerShell 7 and Windows PowerShell 5.1.
- Isolated TESTROOT uninstall identities from the production AppId; retained per-user privileges and fail-before-deployment registry preflight.
- Added runtime observation, four actual permission probes, legacy-host installation fixtures and third-party license inventory.
- Unified desktop and packaged history under the user's .context-notes-native directory; blocked silent empty replacements and added explicit backup import into new destinations.
- Fixed resumed AI questions binding to a previous turn's token-usage notification, which could discard valid deltas/completion and falsely time out. Informational notifications no longer bind active turns; early genuine completion remains supported.
- Preserved all existing tests: 106 xUnit, 80 self-tests and 19 installation assertions; release audits now also check staging files, runtime signature state and source revision existence.

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
