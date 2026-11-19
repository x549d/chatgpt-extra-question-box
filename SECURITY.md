# Security Policy

## Supported Versions

| Version | Supported |
| --- | --- |
| `0.3.x` | Yes |
| `< 0.3.0` | No |

## Reporting a Vulnerability

If you discover a security vulnerability in **Context Notes Native**, please **do not** disclose exploit details in a public issue before the maintainer has had a chance to investigate and prepare a fix.

Please report vulnerabilities through one of the following channels:

1. **Preferred**: Open a private [GitHub Security Advisory](https://github.com/x549d/context-notes-native/security/advisories/new) on `x549d/context-notes-native`.
2. **Alternative (non-sensitive security questions)**: Open an issue at <https://github.com/x549d/context-notes-native/issues>.

When reporting, please include:

- Affected version (for example, `0.3.0`) and Windows version
- Reproduction steps or proof of concept
- Potential security or privacy impact
- Suggested mitigation or patch, if available

Please do **not** attach your personal SQLite conversation database (`%USERPROFILE%\.context-notes-native\data\history.db`), Codex authentication files, or raw conversation logs.

## Security Architecture Summary

- **Official App Server Bridge Only**: Communicates solely with the locally installed official `codex.exe app-server` over stdio JSON-RPC 2.0 using a read-only sandbox policy (`approvalPolicy: "never"`, `sandboxPolicy: readOnly`, `networkAccess: false` for tool calls) inside an empty working directory, bound to a Win32 Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`).
- **No Credential Handling**: Does not read browser cookies, extract process tokens, implement OAuth flows, or store OpenAI/ChatGPT credentials. Account isolation uses irreversible SHA-256 scope fingerprints in memory.
- **Zero Production Network Stack**: `SelectionProbe` has zero production NuGet packages and no `HttpClient`, `WebRequest`, `Socket`, or telemetry logic.
- **Safe Local Persistence**: Parameterized SQLite queries, foreign keys, WAL mode, `PRAGMA synchronous = FULL`, and SQLite Backup API for upgrades/exports. Note that local SQLite files and backups are not application-encrypted and inherit Windows user-profile ACLs.
- **Safe Markdown Rendering**: Does not execute HTML, scripts, or load remote images; only explicit user clicks on `http(s)` links are opened by the OS browser.
- **Code Signing Status**: `0.3.0` Windows binaries and installer are currently **unsigned (`NotSigned`)**. Always verify downloaded packages against the published `SHA256SUMS` file. See [docs/SIGNING-PLAN.md](docs/SIGNING-PLAN.md) for signing status and roadmap.

For detailed technical security controls and data boundaries, see [docs/SECURITY.md](docs/SECURITY.md) and [PRIVACY.md](PRIVACY.md).
