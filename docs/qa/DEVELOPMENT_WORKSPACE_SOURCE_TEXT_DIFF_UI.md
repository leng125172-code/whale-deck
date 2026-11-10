# 工作空间逐行差异的 BFF 和 Vue 验收

2026-11-10，P3-14，代码提交 `f2806fbbabb1ca5ae7af9738bf204af3e593e687`。

所有者可以在暂存清单中展开单文件逐行差异。查询使用带 CSRF 校验的 JSON 请求，以容纳长路径。BFF 在读前后复查会话、开发权限和计划，Gateway 核对签名 Agent 回执，浏览器重新核对范围并将正文作为纯文本渲染。翻页、刷新、撤权和计划失效会清除旧正文。

- 真实 Kestrel、隔离 PostgreSQL、生产 Gateway 与 Unix Socket gRPC 通过。受控宿主验证正确正文、凭据分类无正文、无 CSRF 拒绝、无效相对路径、错误路径回执、查询中撤权及发布确认后的拒绝。
- 生产构建的 Chrome 联合真实 Kestrel、Worker 和隔离 PostgreSQL 验证 Unicode、320 像素暗色布局、HTML 片段纯文本显示、翻页清除、凭据分类及异常回执拒绝。此轮浏览器使用受控宿主，真实签名 Agent 与 ext4 的逐行内容另见[来宾验收](DEVELOPMENT_WORKSPACE_SOURCE_TEXT_DIFF_RPC.md)。
- 常规 .NET 集成测试 837 项通过，7 项原生用例按默认配置跳过。隔离 PostgreSQL 整链专项通过。前端 1,479 项、范围检查 8 项、构建、格式和 lint 通过。隔离数据库已清理。
- 合并、大工作树性能及物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-text-diff-ui-f2806fbbabb1ca5ae7af9738bf204af3e593e687/`。包内清单已复核，SHA-256 为 `6a99c95c908eb5f9e6855509f6fb214e4c793fd62f4aaf3183d15d70e5d32288`。
