# 开发空间真实退役与预算复用

2026-11-11。此批完成正常退役用户闭环，涉及 P3-13/P3-14/P3-16/P3-17/P3-18 的子门槛；完整主任务仍 **0/59**。17 项移出任务不恢复、不计完成，ST272/PET6/供应商与 Live2D16 继续。

## 实现、发布和验收

- 已部署的用户功能提交为 `6d87fde1a1e36e20452b65e241c686ace246dfe9`，准确 CI [42913288135](https://github.com/leng125172-code/whale-deck/actions/runs/42913288135) **13/13 success**。通过固定维护入口执行一次 dev 更新，退出码 0，无自动回滚或重发更新。
- 六组件实际二进制/镜像来源、23 个安装 development 模块逐字节核对通过，11 核心服务健康；数据库平台八容器身份及 PID 1 的双监听身份保持，原五空间、原报告、审计、存储配置及 user NOPASSWD 保留。
- 本次升级连续性 **未通过**：473 个样本为 448×200、24×维护 HTML503、1×`/health/live` HTML502，连接错误 0。独立安装/来源验收明确记录 `httpContinuityPassed=false`，不把升级退出成功或当前健康改写成无断档。后置核对器缺失 `uuid` 导入和只读 Git 继承限制 umask 导致 index 模式 0640 的失败均保留；仅将已知 index 恢复 0664，原 user 所有者保持，后续只读 Git 使用 `GIT_OPTIONAL_LOCKS=0`。
- 本批新验收入口 `validate_workspace_retirement_business_installed.py` 接续原真实 runtime 流程，仅处理唯一新 QA slug 及其 `-reuse` 空间。原两个硬编码留存空间的启停步骤明确跳过；不写 SQL、不直接调用删除内核、不合成浏览器响应、不删除历史或审计。

## 真实身份与依赖闭环

1. 临时真实 Authentik 管理员通过可见登录进入 BFF。UI 登记新空间 `82a27df9-ea84-4bcc-9a92-531b997b07ea`，预算为 1000 毫核、2048 MiB 内存、8192 MiB 磁盘；签名分配、内核项目配额和 Runner 存储核验通过。
2. 实际分块上传 TAR，经 UI 核对并明确发布。归档包含测试源码及公开离线 NuGet 包，没有凭据、预建程序集或预生成测试结果。
3. Node test、.NET build、.NET test 均实际 `Succeeded`；Node 与 xUnit 分别 **2 passed / 0 failed / 0 skipped**，报告解析完整性 `Matched`。JSON/Markdown 报告下载摘要匹配。启停恢复后的额外 Node test 也通过，合计四次成功运行。构建通过没有代替测试通过。
4. 仅该新空间再次停用；HDD 捕获及独立 Verify 成功。归档 ID `26244587-bc48-4aad-9ee5-87d183d7bdc5`。先证明未勾选删除的确认请求 400，再由 UI 独立勾选删除源码、释放配额与预算并提交准确计划摘要。
5. 退役 `6009d2a2-6258-453c-a621-df96cfdb8375` 实际 `Succeeded/Retired`。独立只读检查确认源 allocation 和 detached source 不存在、原 claim 不存在、内核配额限额/用量 **八项全零**；执行 journal 的 request、binding、retirementId、workspaceId、archiveId、projectId 和 quota-released checkpoint 匹配。HDD 副本文件摘要、inode、设备和元数据前后保持。
6. PostgreSQL completion 与 `development.workspace.retirement.budget-released` 审计存在；应用预算按与生产登记器一致的 `NOT EXISTS completion` 规则计算，精确减少本空间预算。退役后四次运行的八次双格式报告下载仍可读取且摘要匹配。
7. 同一真实身份经 BFF 用相同预算登记复用空间 `32db3d46-84ab-4f14-83c2-79bb4e7e75c2`，预算合计精确恢复到第一次退役前。该空间分配后，以同一产品停用、归档、独立确认、退役流程收尾，保留空空间归档和全部历史；八项内核配额和 claim 再次释放。预算最终恢复原五空间合计 **5000 毫核 / 10240 MiB / 40960 MiB**。
8. 普通真实账号读取另一空间 404、报告 403、退役入口 403。双端正常退出；两名临时账号删除并确认不存在。四个实际退役 plan/execute task 的持久请求/宿主回执与 BFF 证明逐项相等，MainPID=0、cgroup 无进程，验收控制单元退出；产品活动任务与文件锁为零。

120,000,000,000 字节 NVMe 与 300,000,000,000 字节 HDD 预分配磁盘镜像、ext4/prjquota 和文件系统容量保持真实强制限制。原五空间的源码、内核配额和生命周期 marker 完整保持，原不可变记录没有减少。

## 失败保留与界面

首轮浏览器观察器把同路径自动 GET 误当成 POST，进程在退役派发前失败。只读查询确认空间仍 Stopped、workflow/completion 均为 0、无旧浏览器进程；修改为直接放行非 POST，保留原账号、空间、归档、首次源码与失败输出后显式续验成功。复用登记请求的幂等键和命令在转发前落盘，避免丢回复后换键撞 slug。

真实 320 暗色和 1440 明色退役页面截图通过，320 暗色图已人工查看：状态、已释放预算及原登记值可读，摘要自动换行，无横向滚动，不显示 Runner 核验或执行入口。

## 封存与后续

- 发布证据 `deploy/artifacts/workspace-retirement-user-release-6d87fde1-20261111/`：34 文件、4,847,053 字节；manifest SHA256 `927c2ad3c057f7f49734b45a1c8e6a6a25e182b1531ef30aff215ca994fcdcd8`。包括准确 CI、唯一更新日志/HTTP 502、独立安装核验及核对器失败/修正。
- 真实闭环 `deploy/artifacts/workspace-retirement-real-user-6d87fde1-20261111/`：70 文件、24,732,367 字节；manifest SHA256 `b17dd2a48e92c221a5bfa6149289bd1b80f857830996bca8ad6145901b50e086`。包括两轮验收源码与摘要、首轮失败、原请求显式续验、真实测试报告、前后全局/物理快照、持久完成与任务收尾、退出及账号清理、实图。两目录已封存，不再追加修改。

升级 502 的候选修复及 HTTP 回归见 [维护上游错误边界](MAINTENANCE_UPSTREAM_HTML_RESPONSE.md)，仍需其准确 CI、标准发布和真实升级连续性重验；不改写本次失败。破坏性退役中断后的显式恢复仍需新的尝试账本及原计划/engine 绑定，不能覆盖旧失败 task 或自动重发删除。其余 59 项及全部子门槛、交付 master/准确 CI 均继续。

用户原有 `deploy/host/whaledeck-development-source.py` 改动继续保留并排除提交，SHA256 `1b79e42155e6e894c80e16bfc537a1c5cce50c718d6653d3b761df1348de20d6`。项目图谱工具不可用，非敏感阶段事件在 pending，**待同步**。
