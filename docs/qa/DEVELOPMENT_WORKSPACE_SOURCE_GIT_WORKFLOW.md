# Git 导入授权、持久化与租约恢复

2026-11-10，代码 `965818bc`。源码导入 BFF 列出当前工作空间所有者获准使用的仓库 ID。Git 登记只接收仓库 ID 和 ref，私有凭据引用由 API 与 Worker 共用的服务端策略决定。Worker 在抓取前、接收清单时、完成获取时以及进入暂存与发布前复核相应权限。安装侧配置见[开发空间 Git 策略](../../deploy/install/README.md)。

首个有效清单固定 Git 对象、剥离后的提交、归档 SHA-256、字节数和文件数。后续重试向 Agent 请求固定对象，并核对完整清单和已有加密分块。仅在 gRPC 正常结束且全量摘要匹配后，导入才能进入待暂存状态。等待抓取期间，Worker 检查租约并中止已取消的 RPC。SQL 迁移保留旧上传记录，有 Git 历史时拒绝降级移除来源字段。

## 实际验证

- 通过真实 Kestrel HTTP、Unix Socket 上的 HTTP/2 gRPC、生产 AgentGateway、Worker 调度器与隔离 PostgreSQL，验证 CSRF、仓库主体授权、请求中夹带凭据引用的拒绝，以及已有记录的幂等重放。受控 gRPC 对端核对实际 HMAC 签名，并按场景注入断流和错误摘要。
- 首块入库后中断读取并保留旧租约。核对首块密文、固定标签和提交，旧租约到期后拒绝写入，新领取按标签对象 ID 恢复，持久化的首块保持一致。等待首帧的获取可从 BFF 取消，RPC 在租约监视器发现状态变化后结束。错误摘要和提前取消均清除暂存数据。
- 完整归档继续走既有暂存与显式确认。业务验收观察到一次暂存与一次确认发布，暂存后密文块已清理。该 PostgreSQL 业务验收使用受控宿主对端。真实签名 Agent 与 ext4 发布另见[宿主验收](DEVELOPMENT_WORKSPACE_SOURCE_GIT_RPC.md)。
- 独立隔离 TLS Git 服务验证同一附注标签按原 ref 和固定对象 ID 获取的 TAR 逐字节相同。真实 PostgreSQL 联合回归通过，6,227 项 .NET 单测、837 项默认集成测试、8 项范围基线、Compose 授权键解析和 EF 模型校验通过。默认集成套件有 7 项原生环境门禁跳过，其中 PostgreSQL 联合用例已另行执行。隔离 PostgreSQL 和 Git 容器资源已清理。

初次构建发现 EF 生成迁移的命名空间不符合仓库风格，随后发现测试夹具中 `Directory` 类型名冲突。修正后构建和原生验证通过。

继续 Vue Git 导入与恢复、来源历史、已有工作树更新及冲突处理和物理部署验收。

私有证据位于 `deploy/artifacts/development-workspace-source-git-workflow-965818bc/`，清单 SHA-256 为 `20bf08605c753fd11c3764ffe3a3339e21fe6e25a85f2392cb1e6f9f1a75028d`。
