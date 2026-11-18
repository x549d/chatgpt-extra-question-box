# 0.3.0 签名状态及后续步骤

当前安装器和本项目运行文件没有 Authenticode 可信签名。当前用户及本机个人证书存储中未发现可用于本项目的代码签名证书；没有生成自签名来冒充可信发布者，没有导出密钥，也没有购买服务。

正式签名前，需要维护者确认发布者法律身份、LICENSE 分发条款和隐私联系方式。维护者可评估受信任 CA 的代码签名证书，或 Microsoft Artifact Signing；申请涉及身份核验，资格、地区和费用由服务提供方当时规则决定，本任务未注册、购买或代为作承诺。

有可用证书后：

1. 明确选择有 Code Signing EKU、有效期内、含可用私钥且证书链受 Windows 信任的证书或云证书配置，记录主体、指纹和有效期。
2. 在生成载荷哈希前，用 Windows SDK SignTool 以 SHA-256 签名 EXE/DLL，使用证书服务方提供的 RFC 3161 时间戳端点（`/tr`、`/td SHA256`）。不要把私钥、PFX 密码或服务访问凭据写入仓库。
3. 编译并签名 setup.exe，随后重新生成便携 ZIP、SHA256.json 和 SHA256SUMS；不可签名后沿用旧哈希。
4. 用 `signtool verify /pa /all /v` 和 `Get-AuthenticodeSignature` 核对状态、主体及时间戳，再执行全套安装与真实桌面验收。当前 Test-Release 明确验证“未签名预览”，正式签名流程必须同时更新该门禁，不能只改状态标签。

参考：[Microsoft 代码签名选项](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)、[Artifact Signing 身份验证流程](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)、[Authenticode 验证](https://learn.microsoft.com/powershell/module/microsoft.powershell.security/get-authenticodesignature)。
