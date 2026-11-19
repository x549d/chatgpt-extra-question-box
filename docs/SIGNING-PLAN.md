# 0.3.0 代码签名状态与后续方案

## Phase 8 状态复核（2026-11-19）

- 再次只读检查当前机器 `Cert:\CurrentUser\My` 与 `Cert:\LocalMachine\My`：有效期内、含可用私钥且具备 Code Signing EKU（`1.3.6.1.5.5.7.3.3`）的证书数量仍为 **0**。
- 本次公开发布的 MIT 0.3.0 `ContextNotesNative-0.3.0-setup.exe` 以及载荷 `SelectionProbe.exe` / `SelectionProbe.dll` 均为 **未签名（`NotSigned`）**，无签名者信息与 RFC 3161 时间戳。
- 本项目**不生成自签名证书冒充可信签名**，**不声称程序已签名**，并在下载页、`README.md` 与 `RELEASE-README.md` 明确告知用户：运行未签名安装器时 Windows SmartScreen 可能显示“未知发布者”警告，请先核对 `SHA256SUMS` 完整性校验值（注意：SHA-256 哈希用于防篡改校验，不等同于可信发布者签名）。
- 在不采购付费证书的前提下，本次公开发布已开放 MIT 源码、未签名 Windows 安装包与便携 ZIP，以便社区直接核查源码或从源码自行构建。

## 后续获取可信签名证书的规划

此前技术调研与官方参考（供维护者后续评估，未在本阶段采购或绑定付款方式）：

1. **方案选项**：可评估受信任 CA 的标准/EV 代码签名证书（含云 HSM / 硬件令牌密钥保护），或 Microsoft Artifact Signing（公开信任身份验证目前对个人开发者有地区限制，暂不含中国大陆；官方定价与资格须以申请时页面为准，参见 [Artifact Signing 文档](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) 与 [定价页](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku)）。
2. **获得可信证书后的标准签名与发布顺序**：
   - 确认证书具备 Code Signing EKU、处于有效期内、私钥受保护且证书链受 Windows 默认信任，记录证书主体、指纹与有效期。
   - 在生成载荷 `SHA256.json` 之前，使用 Windows SDK `signtool.exe` 对 `SelectionProbe.exe` 与 `SelectionProbe.dll` 执行 SHA-256 Authenticode 签名，并附加 RFC 3161 时间戳（`/fd SHA256 /tr <timestamp-url> /td SHA256`）。严禁将私钥、PFX 密码或云签名凭据写入 Git 仓库。
   - 使用已签名载荷编译生成 `ContextNotesNative-<version>-setup.exe`，再对 `setup.exe` 执行同样的 SHA-256 签名与 RFC 3161 时间戳。
   - 重新生成便携 ZIP、`SHA256.json` 与 `SHA256SUMS`（绝不可在签名后沿用未签名哈希）。
   - 使用 `signtool verify /pa /all /v` 与 `Get-AuthenticodeSignature` 核验所有 EXE/DLL 状态为 `Valid` 且含有效时间戳，同步更新 `scripts/Test-Release.ps1` 的签名门禁并完成回归验证。

参考文档：
- [Microsoft 代码签名选项](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Artifact Signing 快速入门](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Get-AuthenticodeSignature](https://learn.microsoft.com/powershell/module/microsoft.powershell.security/get-authenticodesignature)
