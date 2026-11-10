# 开发空间 .NET 首次恢复与真实测试

日期：2026-11-10。现场起点 `b58168c8`；修复 `66a8598d34a161c8e760cf8d55f2c74e00d6ceea` 已完成准确 CI、标准 dev 覆盖部署和真实身份运行/报告验收。空间退役、HDD 留存仍缺失，不签开发空间完整闭环、59 项或 master。

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

## 标准部署与真实身份验收

准确提交 CI [42761599824](https://github.com/leng125172-code/whale-deck/actions/runs/42761599824) 的 12 路全部 success，包含真实 PostgreSQL/Valkey 和完整六分片浏览器回归。干净运行 checkout 快进至 `66a8598d` 后，标准 `whaledeck-platform-maintenance update` 单元 `whaledeck-dev-publish-66a8598d.service` 成功；Invocation `bf024f67e3be463a9cc7ac67905cc18c`，终态 active/exited、MainPID 0、Result success、ExecMainStatus 0。

独立核对实际六组件及 API/Gateway HTTP 来源均为该提交，11 核心 healthy，8 数据库平台容器身份保持。账本 current=`66a8598d`、previous=`b58168c8`、pending=null。标准 host validate 通过；私密运行配置、存储声明及已授权 sudo 规则保持。维护入口采样 146 次均 HTTP 200，覆盖 05:39:39–05:44:30 UTC；只签该采样窗口，不宣称零停机。

再次从实际内核核对 NVMe/HDD 的 120,000,000,000/300,000,000,000 字节预分配映像上限及两处 project quota enforcing。新专用空间 `df31e4e1-8a7e-4ffe-9bef-246ce9b6f218` 经真实 Authentik 管理员、Vue/BFF、Worker、签名 Agent 分配，8 GiB 硬配额独立回读为 8,589,934,592 字节。源归档包含真实测试代码与固定公开离线依赖，经分块上传、暂存计划和页面确认后 Published：`19c59324-3247-4de2-b220-e64cc6d431f6`。

| 真实任务 | Run ID | 实际结果 |
| --- | --- | --- |
| Node test | `8502a5d7-a4e5-42f6-b303-424eeb8fd731` | Succeeded / exit 0，JUnit 2/2 通过，零跳过 |
| .NET build | `3339998a-837f-4f65-831c-ee6eccd1a75f` | Succeeded / exit 0；该步骤不计测试通过 |
| .NET test | `f96f16e7-0548-410e-8b3b-8968b57f1f31` | Succeeded / exit 0，TRX 2/2 通过，零跳过 |

三项均完成 Worker 报告处理，实际 JSON/Markdown 下载摘要匹配，报告页面已打开并截图；测试工件完整性为 Matched、解析状态 Parsed，独立重读下载 JSON 后再次检查实际计数。普通身份读取他人空间/报告被拒绝，缺 CSRF/错误 Origin 被拒绝；正常页面退出完成 BFF 与身份侧失效，两个临时账号已删除。运行束与执行 cgroup 均已消失，两个原始测试报告经受支持的报告退役流程删除并保留回执；数据库三项 reportStage=Complete，全局活动 job 与文件锁均为 0。工作树、空间配额、任务、下载报告和审计保留，尚未执行空间退役。

首轮真实验收在选择 .NET 构建前超时：截图显示 API_RATE_LIMITED，验收脚本轮询与页面轮询叠加超过 120 次/分钟。原截图和 process-1 失败保留；仅将验收脚本改为 3 秒轮询并遵守 Retry-After，保持服务端限制，沿用原测试身份与同一空间的第二轮通过。用于排查的独立 unshare 网络探针不是 runc Runner 复现；实机 .NET 测试实际通过，因此没有据此修改产品网络隔离。

原件：`deploy/artifacts/development-runtime-release-66a8598d-20261110/`（CI、发布、来源、配置及容量核验）和 `deploy/artifacts/development-installed-runtime-00e0f96d51f141c08dcc9e5da9a472ba/`（原失败、两轮脚本字节、UI、双格式报告、配额、数据库与执行资源收尾）。两者均为私有证据，凭据只在内存/stdin，不写入证据。

分别封存 15、33 份原件，SHA256SUMS 摘要为 `59d390020505fea3efd1e19a4c27d12b8fbfe29fed6dbf420fc489cc502520d9`、`30c8b5be352060e4ea20d7c1a5bea52c1b139245639894c8321a3095989f8847`。

## 待完成

完整产品停用/退役、HDD 留存及空间配额释放仍需实现和验收；现有两个专用空间和原失败证据继续保留。当前报告的源码指纹/Git 提交仍为未知，不把成功运行报告算成源码来源、调试、覆盖率或完整 P3-17 验收。

退役不能只加状态后删除目录：现有 run/source 写入尚无共享的 workspace 生命周期锁；必须补充事务内状态围栏、后台任务排空，以及阻止迟到分配 RPC 复活空间的宿主持久屏障。只有独立证明数据/资源已收尾后才释放注册预算；保留 workspace 终态、原幂等键、run/source/job/event/report 与审计。HDD 目录存在不算备份成功。

后续状态更正：上述共享入场锁缺口已由 4ad7669a 的事务 FOR SHARE/FOR UPDATE 与数据库触发器补齐，见[闲置启停验收](DEVELOPMENT_WORKSPACE_IDLE_LIFECYCLE.md)；不能重复按缺失实现。42ea297d 已再次通过真实运行及启停回归，见[本批记录](DEVELOPMENT_WORKSPACE_ARCHIVES_RELEASE.md)。真正退役、防迟到分配复活、HDD 留存授权及物理/预算释放仍待。
