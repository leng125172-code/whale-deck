# 工作空间逐行差异的签名 Agent 验收

2026-11-10，P3-14，代码提交 `835113157255824463e2a76e2a89af81a25543f6`。

Agent 1.74 提供 `development.source.text-diff`。签名绑定工作空间、导入、前驱、计划摘要、文件路径、方法、时限和一次性 nonce。宿主响应有界，只有文本状态可以携带差异正文。Agent 校验响应路径、计划、正文边界与允许的文本字符。宿主子进程复用 Landlock 和 systemd 的写入限制。

- 隔离 KVM 的实际非 root Agent、Unix Socket、sudo、systemd、Git Smart HTTPS 和 ext4 项目配额通过。验收逐行内容和新增文件，拒绝缺签名、跨方法签名、路径被改写、重放、受保护路径及暂存后工作树变化。
- .NET 单测 6,232 项通过，覆盖错路径和分类状态携带正文的拒绝。首轮旧测试的类型推断歧义已修正并复跑。宿主专项 10 项、范围检查 8 项通过，真实 Git 导入、差异分页、更新恢复和旧树下载通过来宾回归。
- BFF、Vue、合并和物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-text-diff-rpc-835113157255824463e2a76e2a89af81a25543f6/`。包内清单已复核，SHA-256 为 `6403876b3f20a3645009bdd1f800b63924a11693de730b1bfabd1f5aef0d3c71`。
