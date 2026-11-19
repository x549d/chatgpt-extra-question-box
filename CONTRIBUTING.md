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

- Ensure `dotnet build -c Release` succeeds with 0 warnings and 0 errors.
- Ensure all xUnit tests and `--self-test` pass.
- Run `git diff --check` and confirm no local databases, logs, build caches, or personal paths are staged.
- Update relevant documentation (`README.md`, `CHANGELOG.md`, `PRIVACY.md`, `docs/THIRD-PARTY-NOTICES.md`) if user-facing behavior, data handling, or dependencies change.

## Licensing of Contributions

By submitting a pull request to this repository, you confirm that you have the right to contribute the code and agree that your contributions will be licensed under the project's [MIT License](LICENSE).
