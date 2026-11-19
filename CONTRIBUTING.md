# Contributing to Context Notes Native

Thank you for your interest in contributing to **Context Notes Native**!

## Project Scope & Architecture

Context Notes Native is a Windows native companion for Codex Desktop Chat built with:

- C# / .NET 10 / WPF
- Windows UI Automation (UIA) & Win32 APIs
- Official Codex App Server (`codex.exe app-server` over stdio JSON-RPC 2.0)
- Windows system SQLite (`winsqlite3.dll` via P/Invoke)
- PowerShell deployment scripts & Inno Setup installer

## Prerequisites

- Windows 10 (2004+) or Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/) (with Windows Desktop targeting)
- Codex Desktop (optional for unit tests; required for live desktop/App Server verification)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (only required when building `setup.exe`)

## Build & Test Locally

```powershell
# Build Release solution (must pass with 0 warnings, 0 errors)
dotnet build ChatGptContextNotesNative.sln -c Release -p:NuGetAudit=false

# Run xUnit test suite (106 tests)
dotnet test ChatGptContextNotesNative.sln -c Release --no-build

# Run built-in self-tests (80 assertions)
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test
```

## Non-Negotiable Security & Privacy Rules

All contributions must preserve these core design boundaries:

1. **No Process Injection or Binary Patching**: Never inject DLLs into Codex Desktop or modify Codex installation files.
2. **No Credential Extraction**: Never read browser cookies, process memory, OAuth tokens, refresh tokens, or `auth.json`. Always communicate through the official `codex.exe app-server` stdio JSON-RPC interface.
3. **No Silent Telemetry**: Do not add analytics, telemetry beacons, or hidden network calls (`HttpClient`, `WebRequest`, `Socket`).
4. **Metadata-Only Diagnostics**: Diagnostic logs must never record selected text, user questions, assistant answers, or authentication secrets.
5. **User Data Safety**: Never delete or overwrite `%USERPROFILE%\.context-notes-native\data\history.db` during tests or upgrades. Automated tests must always use isolated temporary directories and databases.
6. **Zero Unnecessary Dependencies**: Keep `src/SelectionProbe/SelectionProbe.csproj` free of production NuGet dependencies unless explicitly discussed and documented in `docs/THIRD-PARTY-NOTICES.md`.

## Pull Request Checklist

- Ensure `dotnet build ChatGptContextNotesNative.sln -c Release -warnaserror` succeeds with 0 warnings and 0 errors.
- Ensure all xUnit tests and `--self-test` pass (and Windows CI is green).
- Run `git diff --check` and confirm no local databases, logs, build caches, personal emails, Windows usernames, or local absolute paths are staged.
- Update relevant documentation (`README.md`, `CHANGELOG.md`, `PRIVACY.md`, `docs/THIRD-PARTY-NOTICES.md`) if user-facing behavior, data handling, or dependencies change.

## Code Review Guidelines

Reviewers and maintainers evaluate every Pull Request against the following criteria:

1. **Architecture & UIA/Win32 Discipline**:
   - Selection overlay must remain non-activating (`WS_EX_NOACTIVATE`), must not touch the system clipboard, and must isolate UIA calls inside the timeout-guarded worker process.
   - Multi-DPI and negative-origin multi-monitor geometry must stay clamped within the active work area.
2. **Data Integrity & User Safety**:
   - Any SQLite schema change must provide an idempotent, transactional migration with a pre-migration online backup (`HistoryDatabase.BackupFile`) and rollback test coverage.
   - Uninstall and upgrade flows must preserve `%USERPROFILE%\.context-notes-native\data\history.db` by default unless explicitly requested by the user.
3. **Security & Privacy Hygiene**:
   - Verify 0 new production network APIs, 0 credential/cookie readers, and metadata-only diagnostic logging.
   - Verify no personal email, workstation username, or local absolute path is introduced in code, scripts, docs, or commit headers.
4. **Minimalism & Maintainability**:
   - Keep `SelectionProbe.csproj` at 0 production NuGet dependencies unless explicitly agreed.
   - Prefer deterministic unit tests and isolated temporary directories over host-coupled tests.

## Version Release Process

Releases are cut manually by the maintainer only after all verification gates pass (CI never auto-publishes a public Release):

1. **Version & Changelog Alignment**:
   - Synchronize the semantic version across `src/SelectionProbe/SelectionProbe.csproj` (`<Version>`), `codex-plugin/plugins/context-notes/.codex-plugin/plugin.json`, and `CHANGELOG.md`.
   - Ensure `LICENSE` (MIT) and `docs/THIRD-PARTY-NOTICES.md` accurately reflect all components.
2. **Privacy-Clean Build & Packaging**:
   - Build from a CLEAN commit on `main` using `scripts/Build-Release.ps1 -Compiler <Medium-integrity ISCC.exe> -OutputDir <Medium-integrity dir>`.
   - Confirm `dotnet publish` runs with `-p:DebugType=none` and produces 0 `.pdb` files and 0 CodeView RSDS local build paths in `SelectionProbe.dll`.
3. **Full Release Verification Gates**:
   - `dotnet build -c Release -warnaserror` (0 warnings / 0 errors) and `dotnet test -c Release` (all xUnit tests PASS).
   - `SelectionProbe.dll --self-test` (all assertions PASS).
   - `scripts/Test-Release.ps1` (all release audit checks PASS).
   - `scripts/Test-StandardInstaller.ps1` (isolated `/TESTROOT` EXE install/upgrade/rollback/uninstall PASS).
   - `scripts/Test-Installation.ps1` (isolated portable install/upgrade/uninstall PASS).
   - `gitleaks` full git history & payload scan (0 leaks) plus binary/ZIP privacy path check (0 hits).
4. **Tag & Publish**:
   - Create an annotated tag (`git tag -a vX.Y.Z -m "..."`) using the maintainer's GitHub noreply email and push the tag.
   - Publish the GitHub Release attaching `ContextNotesNative-X.Y.Z-setup.exe`, `ContextNotesNative-X.Y.Z-portable.zip`, and `SHA256SUMS`, with honest disclosure of Authenticode signing status (`NotSigned` unless signed by a trusted CA) and test coverage scope.

## Issue Reporting & Real User Feedback

Use the structured templates in [GitHub Issues](https://github.com/x549d/context-notes-native/issues/new/choose):

- **Bug Report**: crashes, streaming issues, UI glitches, or history bugs.
- **Installation Problem**: `setup.exe`, `portable.zip`, plugin/hook registration, or SmartScreen/permission errors.
- **Compatibility Report**: feedback on real Codex Desktop versions, Windows builds, and multi-DPI setups.
- **Feature Request**: workflow improvements aligned with the project's non-invasive architecture.

## Licensing of Contributions

By submitting a pull request to this repository, you confirm that you have the right to contribute the code and agree that your contributions will be licensed under the project's [MIT License](LICENSE).
