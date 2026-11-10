# 开发空间 .NET 首次恢复与真实测试

日期：2026-11-10。生产现场起点 `b58168c8`，本批修复尚待准确提交 CI、标准 dev 部署和实际身份验收；不签开发空间完整闭环、59 项或 master。

## 原失败与定位

原安装态空间 `c23526c5-fc43-4944-9f27-75ac540f6df0` 的 Node 运行 `594b791c-7642-4945-9b39-347c51e0c026` 成功，并完成双格式报告下载。.NET 构建 `2cfe61ea-4e27-4ff7-a2fb-1035c4cf5fe9` 的宿主原回执是 `Failed`、退出码 1，被分派器映射为 `WORKSPACE_RUN_HOST_FAILED`。原任务、源码、审计、报告和宿主回执保留。该次宿主没有保存编译器输出，因此不声称原失败包含已留存的编译日志。

在专用副本中，用同一固定 SDK、真实原项目源码及非 root、断网、只读根、资源受限的进程复现：`dotnet build --no-restore` 退出 1，实际输出 `NETSDK1004`；去掉该标志后构建退出 0。首次导入没有 `obj/project.assets.json`，原命令禁止首次恢复。原工作空间没有被修改。原件在 `deploy/artifacts/dotnet-restore-repro-496d97562fde4f9c8eb8f29bac276fe5/`。

## 修复与边界

固定 .NET build/test 命令允许首次恢复，Runner 网络隔离保持，外部包须作为已导入源码的离线依赖提供；未增加联网代理或任意命令入口。包缓存固定在 `/work/.nuget/packages`，计入空间硬配额，避免正常依赖占用 64 MiB 临时盘。合法非零任务回执标记 `TaskFailed` / `WORKSPACE_RUN_TASK_FAILED`，原历史失败不改写。

Agent 新增协议 1.75 和 `development.run.dotnet-restore`。新命令在运行、观察、取消、回收及报告转移入口要求完整能力；旧版 Agent 在业务 RPC 前被拒绝。旧 `--no-restore` 测试回执继续识别，旧 Node 运行不抬高最低版本。

## 本地与隔离验证

- 6234 项 Unit 通过、零跳过；扩充后的 5 项 Runner RPC 专项通过，含真实 Unix socket 的四入口能力拒绝。
- 837 项 Integration 通过，7 项依赖原生服务的测试默认跳过；跳过项不计验收，由准确提交的原生 CI 和实机验收继续覆盖。
- root 执行 12 项 Runner/隔离宿主测试全部通过。
- 全新 xUnit 项目使用与仓库相同的固定公开测试依赖，从已有公开包缓存生成 13 个离线包，未导入凭据、NuGet 配置、预编译产物或旧测试输出。在固定 SDK 的非 root、断网、只读根、CPU/内存/PID 受限环境下，冷恢复并实际运行 **2/2 测试通过，零跳过**，独立解析 TRX 计数。证据：`deploy/artifacts/dotnet-offline-tests-ef0b21089cb048818401059840cd75e2/accepted.json`、`cold-test.log`、`packages.json` 和真实 TRX。
- 首轮编译检查发现 `CA1861`，将重复使用的命令数组改成静态只读字段后通过；没有屏蔽分析器。

实机脚本扩充为 Node test → .NET build → .NET test，检查每项的独立报告、内容摘要、真实测试计数及 UI；通过构建不替代测试。该脚本使用真实 Authentik 临时身份，失败时保留有界修复窗口，最终删除临时身份，工作空间等待产品退役流程，禁止直接 SQL 删除或手工抹除审计。

## 待完成

准确提交 CI、标准 dev 覆盖部署、六组件来源/健康核对与上述实机整链。完整产品空间退役、HDD 留存及资源收尾仍需实现和验收；现有专用空间和原失败证据继续保留。
