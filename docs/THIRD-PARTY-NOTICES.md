# Third-party components and license inventory

Reviewed 2026-11-19. This inventory does not grant a license to Context Notes Native; the root LICENSE still records its existing Proprietary status and requires publisher confirmation before public redistribution.

| Component | Use | License / distribution boundary |
| --- | --- | --- |
| .NET 10 / WPF | Framework-dependent runtime prerequisite | .NET is MIT licensed; the runtime is installed separately, not included in this package. [Official runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| Windows UI Automation / winsqlite3.dll | Windows-provided APIs | Supplied by Windows under its applicable terms. No Windows DLL or separate SQLite binary is redistributed here. |
| Codex Desktop / official App Server | Separate installed prerequisite and authenticated AI service | No Codex executable or credentials are included in this package. The user's applicable OpenAI service/account terms continue to apply. |
| Inno Setup 6.7.4 | Setup compiler and generated installer | The official distribution's license permits use including commercial purposes subject to retaining its notices and identifying modifications. Its copyright/website notices have not been removed. The compiler currently reports no configured commercial license; no paid license was purchased. [Official license](https://github.com/jrsoftware/issrc/blob/main/license.txt) and [commercial program](https://jrsoftware.org/inno-setup/purchase) must be checked for the publisher's intended use. |
| xunit 2.9.3 | Tests only | Local NuGet package metadata declares Apache-2.0. Not included in the release payload. |
| xunit.runner.visualstudio 3.1.5 | Tests only | Local NuGet package metadata declares Apache-2.0. Not included in the release payload. |
| Microsoft.NET.Test.Sdk 18.11.0 | Tests only | Local NuGet package metadata declares MIT. Not included in the release payload. |

The application project has no production NuGet PackageReference, no third-party custom fonts, no bundled icon packs or images, and no redistributed Windows or Codex binaries. The release contains only its own four framework-dependent runtime files, plugin hooks/manifest, the deployment script and user documents. Development tools, test runners and their transitive packages are not shipped. If a future change redistributes a runtime, asset, or production package, update this inventory and include the required notices before distribution.

## Independence and Trademark Notice

This project is an independent third-party Windows desktop utility. It is **not affiliated with, sponsored by, endorsed by, or officially certified by OpenAI**. "OpenAI", "ChatGPT", and "Codex" are trademarks of OpenAI. Before any public distribution, the maintainer must confirm the project's public naming, branding boundaries, distribution license (`LICENSE`), and publisher contact information (`PRIVACY.md`).

Phase 7 review (2026-11-19): [OpenAI's current brand guidelines](https://openai.com/brand/) restrict product/model-brand naming and misleading endorsement. The historical repository title "ChatGPT Context Notes Native" needs an explicit public-branding decision; the independence disclaimer alone does not establish permission. No logo or font has been downloaded and no public rename, legal authorization or trademark permission is assumed. The maintainer can assess a neutral name such as "Context Notes Native" with a factual compatibility description, or supply applicable permission for review.
