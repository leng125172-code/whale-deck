# 工作空间逐文件差异的 BFF 和 Vue 验收

2026-11-10，P3-14，代码提交 `fed7722887956954add170310e71638f775ddee2`。

源码页按当前暂存计划分页展示相对路径、变化类别、当前与候选节点的类型、大小和可执行位。BFF 在查询前后校验所有者、开发权限和暂存计划。Agent Gateway 校验分页和前驱绑定，并与计划的差异总数交叉核对。Vue 验证回执并在计划变化、刷新或撤权时清除旧列表。

- 真实 Kestrel、隔离 PostgreSQL、生产 Gateway 和 Unix Socket gRPC 通过。受控宿主验证签名分页、所有者范围、错计划、回执数量不符、查询中撤销权限和发布确认后的拒绝。
- 生产构建的 Chrome、Kestrel、Worker 与隔离 PostgreSQL 验证前后翻页、Unicode 路径、受保护路径计数、异常游标拒绝和 320 像素暗色布局。宿主使用受控数据，真实 ext4 和签名 Agent 的证据见[上一批验收](DEVELOPMENT_WORKSPACE_SOURCE_DIFF_RPC.md)。
- 1,479 个前端单测、范围检查 8 项、构建和 lint 通过。初次类型检查发现编译目标不支持 `Array.at`，改用普通索引后重新构建并复跑浏览器。隔离数据库已清理。
- 逐行内容差异、合并、真实大工作树性能和物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-diff-ui-fed7722887956954add170310e71638f775ddee2/`。包内清单已复核，SHA-256 为 `72949c56aad3bdcdb894ffd12f4f271516b67d6534559077c18d0db8c67831ba`。
