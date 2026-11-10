# 工作空间旧树所有者下载验收

2026-11-10，P3-14，代码提交 `4717189789464634f10ad00e1712bd5591f15b05`。

源码页从当前发布基线及各次已完成的历史更新链接至 BFF 原生附件。BFF 查询同工作空间及所有者下的发布记录，记录获准导出的审计。响应采用固定 TAR 附件名和禁止缓存。传输前后复查会话及开发权限。Agent Gateway 校验有界分块、连续偏移、长度和 SHA-256，只有 gRPC 正常结束后才放行最后一块。响应开始后发生拒绝或数据错误时，中断 HTTP 连接。

## 验收

- 真实 Kestrel、隔离 PostgreSQL 与生产 Gateway 的 Unix Socket gRPC 链路通过。受控宿主验证签名绑定、有效附件字节、拒绝匿名及其他所有者、首次导入、待确认发布和 Range。测试了错偏移、坏摘要、尾帧后 gRPC 失败和开始响应后撤销开发组，后三种均使真实 HTTP 下载中断。核对六条获准尝试均归属实际所有者。
- 生产构建的 Chrome 联合真实 Kestrel、Worker 和 PostgreSQL，使用受控宿主返回的 TAR。当前基线和历史下载均与原件逐字节一致，首次导入隐藏入口。注入开始响应后的失败，Chrome 报告下载失败。320 像素暗色布局及五个浏览器回归通过。
- 837 个常规 .NET 集成测试通过，7 项原生用例按默认配置跳过。另行开启隔离 PostgreSQL 执行整链用例通过。前端 1,479 项通过，首轮并发执行时两项会话用例失败，限制为四个 Worker 后复跑全绿。格式、lint 和范围检查通过。隔离数据库及浏览器资源已清理。
- 实际宿主归档与签名流见[Agent 验收](DEVELOPMENT_WORKSPACE_SOURCE_PREVIOUS_RPC.md)。本批覆盖 BFF、生产 Gateway 与浏览器，逐文件差异与合并、物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-previous-download-4717189789464634f10ad00e1712bd5591f15b05/`。包内 `SHA256SUMS` 校验通过，清单摘要为 `f97e6f66901064ba55f7b58a0c39a521e5084cdd9523b4ce256d5e465965e9ea`。
