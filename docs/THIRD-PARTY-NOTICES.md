# Third-Party Components, Licenses & Trademark Notices

Last reviewed: 2026-11-19 (Phase 8A/8C).

**Context Notes Native** original source code, scripts, plugin manifests, and documentation are licensed under the **MIT License** (see root [LICENSE](../LICENSE)). The MIT License applies **only** to this project's own original code and does not alter the license terms of any third-party component, runtime, tool, or prerequisite listed below.

---

## 1. Runtime, Operating System & Packaging Components

| Component | Category | License / Terms | Redistributed in Package? | Handling & Notes |
| --- | --- | --- | --- | --- |
| .NET 10 Windows Desktop Runtime / WPF | Runtime prerequisite | [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | **No** (`--no-self-contained`) | Installed separately by the user; not bundled in `setup.exe` or `portable.zip`. |
| Windows APIs (`user32.dll`, `kernel32.dll`, `advapi32.dll`, UI Automation, `winsqlite3.dll`) | OS-provided libraries | Microsoft Windows License Terms | **No** | Invoked via standard P/Invoke / Windows APIs; no Windows or SQLite DLL is redistributed. |
| System Fonts (`Microsoft YaHei UI`, `Segoe UI`, `Cascadia Mono`, `Consolas`) | UI font names | Microsoft Windows License Terms | **No** | Referenced by font family name only; zero font files are bundled. |
| Codex Desktop & official `codex.exe app-server` | Host application & AI service bridge | User's applicable OpenAI Terms & Privacy Policy | **No** | Installed separately by the user; no Codex binaries or credentials are included. |
| Inno Setup 6.7.4 | Installer compiler & setup stub | [Inno Setup License](https://github.com/jrsoftware/issrc/blob/main/license.txt) (Jordan Russell / Martijn Laan) | **Installer stub only (`setup.exe`)** | Official unmodified Inno Setup 6.7.4 compiler was used to generate `ContextNotesNative-0.3.0-setup.exe`. All original Inno Setup copyright and website notices are retained intact. The Inno Setup License governs the installer stub; project code inside the payload is MIT. `portable.zip` contains no Inno Setup components. |

---

## 2. NuGet Packages

### Production Application (`src/SelectionProbe/SelectionProbe.csproj`)

- **0 production NuGet packages** (zero direct or transitive `PackageReference` entries).
- **0 bundled third-party icon packs, images, fonts, or external DLLs**.

### Test-Only Dependencies (`tests/SelectionProbe.Tests/SelectionProbe.Tests.csproj`)

The following 12 packages (3 direct + 9 transitive) are used strictly for unit testing during development and are **never included** in published runtime binaries, `setup.exe`, or `portable.zip`:

| Package | Version | Relationship | Declared License | Copyright / Authors |
| --- | --- | --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | Direct (test-only) | MIT | Microsoft Corporation |
| `Microsoft.CodeCoverage` | 18.11.0 | Transitive (test-only) | MIT | Microsoft Corporation |
| `Microsoft.TestPlatform.ObjectModel` | 18.11.0 | Transitive (test-only) | MIT | Microsoft Corporation |
| `Microsoft.TestPlatform.TestHost` | 18.11.0 | Transitive (test-only) | MIT | Microsoft Corporation |
| `xunit` | 2.9.3 | Direct (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.runner.visualstudio` | 3.1.5 | Direct (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.abstractions` | 2.0.3 | Transitive (test-only) | Apache-2.0 | James Newkirk, Brad Wilson |
| `xunit.analyzers` | 1.18.0 | Transitive (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.assert` | 2.9.3 | Transitive (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.core` | 2.9.3 | Transitive (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.extensibility.core` | 2.9.3 | Transitive (test-only) | Apache-2.0 | .NET Foundation |
| `xunit.extensibility.execution` | 2.9.3 | Transitive (test-only) | Apache-2.0 | .NET Foundation |

---

## 3. Independence & Trademark Notice

Context Notes Native is an independent, community open-source Windows desktop utility. It is **not affiliated with, sponsored by, endorsed by, or officially certified by OpenAI**.

"OpenAI", "ChatGPT", and "Codex" are trademarks of OpenAI. References to "Codex Desktop" or "Codex Chat" are used solely to factually describe interoperability with the user's separately installed Codex Desktop application. No OpenAI logos, brand assets, or proprietary fonts are distributed in this repository or its release packages.
