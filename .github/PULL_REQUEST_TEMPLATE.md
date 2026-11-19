## Summary

<!-- Briefly describe what this PR changes and why. -->

## Related Issue(s)

- Closes / Relates to #

## Type of Change

- [ ] Bug fix
- [ ] New feature
- [ ] Documentation / Demo / Templates
- [ ] CI / Build / Packaging
- [ ] Compatibility fix (Codex Desktop / Windows / DPI / IME)

## Verification & Testing

- [ ] `dotnet build ChatGptContextNotesNative.sln -c Release -warnaserror` (0 warnings, 0 errors)
- [ ] `dotnet test ChatGptContextNotesNative.sln -c Release --no-build` (all xUnit tests pass)
- [ ] `dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test` (all assertions pass)
- [ ] PowerShell syntax check passed (if any `.ps1` modified)
- [ ] `git diff --check` passed

## Security, Privacy & Safety Checklist

- [ ] No process injection, binary patching, or undocumented host hooking introduced
- [ ] No browser cookie, OAuth token, `auth.json`, or process memory extraction
- [ ] No silent telemetry, analytics, or unapproved network calls (`HttpClient`/`WebRequest`/`Socket`)
- [ ] No production user SQLite database (`%USERPROFILE%\.context-notes-native\data\history.db`) touched or deleted by tests
- [ ] No personal email, Windows username, local absolute paths, logs, or `.db`/`.pdb` files included
- [ ] Production `SelectionProbe.csproj` remains at 0 NuGet dependencies (or updated `docs/THIRD-PARTY-NOTICES.md`)
- [ ] Relevant docs (`README.md`, `CHANGELOG.md`, `PRIVACY.md`) updated if user-facing behavior changed
