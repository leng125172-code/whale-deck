# 开发空间持久 HDD 归档业务

2026-11-10 开始，2026-11-11 b46f1bbc 准确 CI 13/13 成功，标准 dev 发布及真实身份安装态 Plan/取消/确认/Capture/独立 Verify/恢复/退出通过。真实撤权与故障矩阵、源码退役及预算释放继续实施；完整主任务仍 0/59。

## 实现范围

停用且已分配的空间通过独立 BFF API 和 Vue 环境页，提交持久 Plan、核对计划摘要后显式确认 Capture，再由另一持久任务执行独立 Verify。OperationJob、归档意图、每阶段唯一 task 和 canonical request、宿主 proof、租约、取消、重授权与人类审计落 PostgreSQL。Capture 回执与 Verify 请求在同一事务绑定，Verify 不能根据客户端提交的任意摘要建立。

每次操作核对当前登录、Authentik 目录授权及本人空间或管理员范围；开始、确认及执行须当前批准存储根。执行前后再次复核。未派发时失权暂停，允许当前有权管理员重授权同一任务；已派发后失权持久取消并精确停止原任务，不继续下一阶段。通用 Job cancel 不得绕过专用流程。全部写入沿用 BFF CSRF/Origin。

预算锁、空间行锁、归档行锁保持固定顺序，RPC 不在数据库事务中。迁移额外对旧 API 的空间 UPDATE/DELETE 设置数据库围栏：归档未取得可信终态前，租约过期、Pending、Running、Finishing、失联或坏回执均不能恢复空间。降级若存在任何归档历史则拒绝，不删除审计。

派发标记必须先提交，未知请求或丢回复后只 Observe 原 task，不新建 UUID 重试。复核发现 Agent capability/key 预检确定未调用归档 RPC 时也会消耗该标记，已修成明确的未派发事实：仅当前未过期租约、无任何宿主 proof/终态且网关能证明未调用归档 RPC 时允许释放派发标记，保留原 task。真正调用 RPC 后的任何错误均不释放。Worker 在提交标记后、调用 RPC 前崩溃的不可判定窗口仍保守 Observe，用户可取消以取得原 task 的宿主 tombstone。

Vue 初始状态未核对或提交回复丢失时保留恢复围栏，自动查询服务器当前归档；刷新和重载恢复原意图。401/403 清除已显示的归档详情并停止轮询，人工刷新成功后恢复。切换空间清除上一空间状态。成功页显示 Capture/Verify 一致的归档大小、归档 SHA-256 和独立校验回执；移动端使用单列与可换行摘要。

源码、历史、NVMe 配额及资源预算始终保留。本批不删除源码、不释放预算，也不把取消、Rejected 或 Interrupted 当作 HDD 已清理；失败或取消可能留下部分 HDD 文件和原日志，须保留并通过后续受支持流程处理。

## 已运行的候选验证

- 全解决方案 Release 构建成功，0 警告、0 错误；6249 全量 .NET 单测通过，15 项归档/启停真实 Unix Socket gRPC 专项通过。
- 837 项普通集成测试通过，无跳过。真实隔离 PostgreSQL 全联合 Fact 实际执行 1、通过 1、跳过 0；此前文件/报告/源码/启停回归以及新增归档 HTTP/CSRF、幂等和竞争确认、原始 SQL 恢复围栏、丢 Worker/租约接管、Start 丢回复、确定未派发预检恢复、坏 Verify 回执、当前撤权与管理员重授权均实际运行。两套隔离数据库已删除。身份目录和 Agent proof 是明确合成替身，不能替代实机身份/HDD 验收。
- 1481 前端单测通过，类型、lint、格式及生产构建通过。真实 Chrome 加生产 Vue bundle、合成且拦截的业务元数据完成 13 项回归：320/1440 明暗、明确勾选确认、Capture 后重载、独立 Verify 回执、全过程恢复围栏、丢回复、取消和重授权、权限拒绝停轮询，以及原启停/报告/上传流程。实际查看 320px 暗色全页图，无水平溢出。
- 首轮 PostgreSQL 归档夹具把新创建时间与旧分配时间拼接，被原存储时间约束 23514 拒绝；保持约束，修正夹具为一致时间后重新完整运行通过。首轮 Node 26.13.0 的前端全量单测因原生 global localStorage 与 jsdom 冲突，16 项失败；记录失败，使用 NODE_OPTIONS=--no-experimental-webstorage 后 1481 项通过。仓库 CI 仍固定 Node 24.15.0，未修改产品或放宽测试。

候选证据目录：deploy/artifacts/development-archive-business-1486f931-20261110/。其中 postgres-66d624e1 为失败原件，postgres-98aa3962 为成功轮，均记录执行及清理结果；不得将失败改记为成功。安装态脚本 validate_workspace_archive_business_installed.py/.mjs 必须显式启用，仅作用于原已指定验收空间。发布后的真实执行见下节。

## 标准发布与真实身份验收

- 准确提交 b46f1bbce6a0b578fa631c111bedab7e4b45e547 的 [CI 42851189379](https://github.com/leng125172-code/whale-deck/actions/runs/42851189379) 第一次运行全部 13 个 job 成功，包括六组原完整浏览器回归、required frontend 汇总与真实 PostgreSQL/Valkey。冻结提交的本机标准 PostgreSQL 联合门禁同样实际执行 1、通过 1、跳过 0，两库清理。
- 正式干净检出通过固定维护入口覆盖 dev，六组件/实际 HTTP 同源，23 个宿主模块及两个运行进程 inode 核对、host validate 和 11 核心 healthy 通过。1,120 次固定维护端点采样为 1,088 次 200、32 次维护 HTML 503，连接错误 0；PID 1 两监听 inode 与激活时间在整个观察窗口保持。只说明采样覆盖的窗口，不推翻首次旧自监听迁移的拒连失败。发布前后原 18,247 条记录、5 空间与 HDD/journal 保持，部署账本 pending=null。
- 通过真实临时 Authentik 管理员、正常 Chrome 登录和已部署 Vue/BFF/Worker/Agent，在原 QA 空间完成 Active epoch15→Stopped16，首个 Plan 幂等重放后取消；第二个 Plan 显式勾选确认后 Capture，并由另一持久 task 独立 Verify，页面重载恢复回执，最后恢复 Active17。全过程原 Resume 计划受归档围栏拒绝，归档结束后恢复成功。正常菜单退出清除 BFF 与身份源会话，临时账号经身份源删除并确认 404。320 暗色与 1440 亮色实机图均已查看，无水平溢出。
- 独立 SQL/实盘复核两个持久意图、4 个唯一阶段 task、canonical request/proof/settled/dispatch 和审计保留。取消计划无 HDD Capture，成功归档 38 项、4,132 字节源码生成 3,025 字节 tar.gz；数据库 Capture/Verify、实际 receipt、文件 SHA-256/大小/inode 一致。新 HDD 副本及全部任务/审计保留，未删除源码或释放预算。
- 18,242 条原不可变记录逐摘要保持，5 个空间除目标预期生命周期字段外登记/资源字段保持，原 18 报告、其他四空间及五空间源码树/inode/硬配额保持；原 56 个归档文件逐摘要保持，前 14 个证据清单/587 文件再次重验。活动 job、路径锁、归档 lease 和各自 cgroup 进程均为 0，成功发布 oneshot 精确停止且 journal 保留。120,000,000,000/300,000,000,000 字节镜像仍完整预留，文件系统容量有界且 NVMe prjquota 保持；user NOPASSWD、原未提交 source.py 差异及两仓库 user:user 0664 index 保持。

安装态执行前复核发现验收脚本原先在收到 Begin 回执后才存 key，丢回复可能漏掉待取消意图；现于转发真实 UI 请求前存原 key/command，异常时按原 key 经产品 API 重放和取消，不猜 ID、不改 SQL、不删源或 HDD。Python wrapper 另独立断言两意图、Capture/Verify 摘要链和正常退出；这版脚本已用于上述成功实机验收。实际丢回复故障注入仍待，不将正常执行当作故障矩阵通过。

独立观察器先误用规划 UUID 而非已格式化 images.uuid，随后把 JSON 恢复的 list 与实时 tuple 直接比较；均保留失败脚本/快照，按真实身份字段和一致 JSON 表示修正后完整复核通过，未放宽产品约束或改变生产数据。候选阶段的 SDK 临时容器也曾误入核心计数，清理完成后原 11 核心断言通过。

安装态私有证据：deploy/artifacts/development-archive-workflow-release-b46f1bbc-20261111/，主要为 ci-passed.json、deployment-observation.json、installed-provenance.json、installed-business/accepted.json、business-guard-after.json 和 batch-acceptance.json；执行脚本原件、截图、失败及精确发布 journal 均保留。

候选 33 文件与安装态 49 文件各由 SHA256-MANIFEST.json 封存并逐字节重验。封存后不再原地追加证据；后续批次使用新目录，保留本次成功和失败原件。

## 剩余门槛

实机正向归档用户链已通过，继续真实撤权/丢回复/重启等适用故障矩阵；本次验收脚本与记录新提交仍需自身准确 CI。空间源码退役、预算释放、HDD 留存及后续删除流程仍须另外实现和验收，已校验副本不构成删除授权。59 项及 ST272/PET6/供应商 Live2D16 其余门槛继续，17 项移出不恢复。图谱仅本地 pending，尚未远程同步。
