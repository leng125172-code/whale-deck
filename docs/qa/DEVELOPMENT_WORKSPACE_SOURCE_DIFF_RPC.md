# 工作空间逐文件差异的签名 Agent 验收

2026-11-10，P3-14，代码提交 `444caf6065ffa942ec9e7d27b402510d29231a59`。

Agent 1.73 提供 `development.source.diff`。请求签名绑定工作空间、当前导入、前驱、计划摘要、偏移、页大小、方法、时限和一次性 nonce。Agent 校验宿主 JSON 的大小、字段、两侧节点形状、路径和分页边界，返回类型化清单。差异进程沿用旧树导出的 Landlock 和 systemd 写入限制。

## 验收

- 隔离 KVM 使用实际非 root Agent、Unix Socket gRPC、sudo、systemd、Git Smart HTTPS 和 ext4 项目配额。真实查询逐页取得正确的文件变化，受保护路径只计数。
- 拒绝缺签名、签名跨方法、分页偏移或页大小被改写和 nonce 重放。对签名正确的错计划、越界偏移、暂存后工作树改变及已发布计划返回对应错误。
- .NET 单测 6,231 项通过，含错误游标、错误前驱、路径形状与重复字段拒绝。宿主专项 8 项、范围检查 8 项通过。真实 Git 导入、更新中断恢复和旧树流通过来宾回归，来宾完成并关机。
- 本批覆盖宿主与 Agent 的逐文件元数据查询。BFF/Vue 授权和分页、逐行差异及合并、物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-diff-rpc-444caf6065ffa942ec9e7d27b402510d29231a59/`。包内 `SHA256SUMS` 校验通过，清单摘要为 `3a01d1d73b6e0ffe79f9bd901585961e39b4ada7b46d7160052333e62b24a8e9`。
