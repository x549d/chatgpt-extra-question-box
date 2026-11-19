# 0.3.0 签名状态及后续步骤

当前安装器和本项目运行文件没有 Authenticode 可信签名。当前用户及本机个人证书存储中未发现可用于本项目的代码签名证书；没有生成自签名来冒充可信发布者，没有导出密钥，也没有购买服务。

正式签名前，需要维护者确认发布者法律身份、LICENSE 分发条款和隐私联系方式。维护者可评估受信任 CA 的代码签名证书，或 Microsoft Artifact Signing；申请涉及身份核验，资格、地区和费用由服务提供方当时规则决定，本任务未注册、购买或代为作承诺。

2026-11-19 重新核对的官方成本示例：Artifact Signing Basic 为每账户每月 USD 9.99，含 5,000 次签名，超额 USD 0.005/次；Premium USD 99.99/月、100,000 次。税费、汇率及实际账单须另核。[官方价格表](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku)。这只是申请方案，不是本次采购。

公开信任身份验证有地区限制：个人开发者目前须位于美国或加拿大，组织须属于官方列明地区；当前列表不含中国大陆，不能假定当前维护者具有资格。维护者应先确认法律身份与资格，再决定创建账户、验证身份、建立 Public Trust 证书配置，或向支持其主体的受信任 CA 取得包含硬件/云密钥保护的具体报价。[官方资格与申请步骤](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)。本轮没有提交身份资料、开通付费资源或使用付费签名。

有可用证书后：

1. 明确选择有 Code Signing EKU、有效期内、含可用私钥且证书链受 Windows 信任的证书或云证书配置，记录主体、指纹和有效期。
2. 在生成载荷哈希前，用 Windows SDK SignTool 以 SHA-256 签名 EXE/DLL，使用证书服务方提供的 RFC 3161 时间戳端点（`/tr`、`/td SHA256`）。不要把私钥、PFX 密码或服务访问凭据写入仓库。
3. 编译并签名 setup.exe，随后重新生成便携 ZIP、SHA256.json 和 SHA256SUMS；不可签名后沿用旧哈希。
4. 用 `signtool verify /pa /all /v` 和 `Get-AuthenticodeSignature` 核对状态、主体及时间戳，再执行全套安装与真实桌面验收。当前 Test-Release 明确验证“未签名预览”，正式签名流程必须同时更新该门禁，不能只改状态标签。

参考：[Microsoft 代码签名选项](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)、[Artifact Signing 身份验证流程](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)、[Authenticode 验证](https://learn.microsoft.com/powershell/module/microsoft.powershell.security/get-authenticodesignature)。
