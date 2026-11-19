# Phase 7A：独立 Windows 环境记录

2026-11-19；状态 **BLOCKED - CLEAN WINDOWS ENVIRONMENT UNAVAILABLE**。没有以本机隔离目录、锁屏或仅服务层测试替代完整独立 OS 验收。

## 本地环境复核

- 宿主为 Windows 11 Home Chinese，10.0.26100；未启用 Sandbox / Hyper-V，未修改 Windows 功能、安全策略或账户。
- 发现自定义路径 `<local-vmware-dir>` 下的 VMware Workstation 17.6.3.24583834；初始及结束时 `vmrun list` 均为 0 个运行中 VM。之前“没有 VMware”的判断仅覆盖常规路径，应以本次复核为准。
- 发现两个现有加密 Windows 11 VM。没有提取密码、密钥或启动原 VM。命令行克隆遇到加密密码提示后停止该克隆进程。
- 使用 VMware 已有解锁状态，从 `<local-vm-dir>\Windows 11 x64.vmx` 的当前状态创建独立 Full Clone，名称 ContextNotes-Phase7-Isolated，目录 `artifacts\phase7-vm-isolated-gui`。只启动此克隆，实际到达 Windows 锁屏，未登录来宾系统，不能证明镜像完全干净或完成安装测试。
- 用户随后改选云环境；本地测试停止。最终只读查询确认无运行 VM；克隆及原 VM 保留，未做递归清理。

## 云环境选择与预算

用户明确要求新建云 Windows 主机、平台由代理选择、**总预算最多 20 元**。截至本记录未下单、未创建云资源、费用 0 元。

用户已自行登录阿里云。控制台显示个人实名认证；我的试用中为 ECS 个人版 300 元节省计划试用，另有模型推理试用，未显示无影 Windows 11 试用。ECS 额度不能直接当作无影云电脑抵扣承诺。

已准备无影企业版杭州按需方案：Windows 11 Pro、企业办公型 6 vCPU / 12 GiB、系统盘 80 GiB、数据盘 0、赠送基础带宽、AI 能力关闭。页面最终报价 **1.11786 元/小时**（计算 1.0901 + 存储 0.02776），不是 89 元月套餐。该价格不含 Windows 11 许可证；关机后存储仍计费，故不能仅以关机作为总预算控制手段。

创建页明确提示 Windows 11 Pro 为 BYOL，创建后需自行取得许可证并激活。用户对适用许可证回复“不清楚，先保留此项待确认”，因此没有购买、接受协议或配置权限。控制台及配置页保留供后续接续。

官方依据：[Windows 11 镜像及 BYOL](https://help.aliyun.com/zh/wuying-workspace/user-guide/create-cloud-computer-3)、[按需计费](https://help.aliyun.com/zh/wuying-workspace/pay-as-you-go)、[ECS 公共镜像](https://help.aliyun.com/zh/ecs/user-guide/public-mirroring-overview/)。ECS 官方 Windows 公共镜像为 Windows Server；[无影个人版说明](https://help.aliyun.com/zh/edsp/get-a-personal-cloud-computer)也明确不支持 Windows 10/11 和 Microsoft Store/Codex，不能用该免费试用冒充目标桌面环境。

## 验收矩阵与接续

| 项目 | 当前结果 |
| --- | --- |
| 独立目标 Windows 可登录、可运行官方 Codex Desktop | NOT TESTED |
| 全新安装、插件/Hook、Companion/Watcher | NOT TESTED |
| 实际选区、旁注、AI、SQLite 历史 | NOT TESTED |
| 原地升级、迁移、缓存更新、历史保留及重启 | NOT TESTED |
| 卸载进程/插件/Hook/文件清理、默认保留历史 | NOT TESTED |

接续须先确认适用 Windows 11 云使用许可，再核对实时总价和试用抵扣范围。如仍使用按需资源，应预留验证/导出时间并在预算内释放本次测试资源，避免磁盘持续计费；任何新条款或权限扩展在实际发生前单独确认。然后仅在新环境完成上述全流程，使用合成历史，不上传宿主真实用户数据库或认证文件。不要重新运行 Phase 6 30/60 分钟采样及已通过的宿主安装器专项。
