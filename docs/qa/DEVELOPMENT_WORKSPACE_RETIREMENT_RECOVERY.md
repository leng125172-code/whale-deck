# 开发空间退役：显式恢复与真实拒绝恢复验收

接续：2026-11-11，356ba6c1新QA真实部分删除后中断：4文件已删/502仍在、缺失均具原意图，原716条manifest绑定；真实Interrupted保留预算，UI新task沿原日志恢复及两空间退役、预算复用、报告和账号收尾首次通过。a90bc7d2准确CI全13，本批新提交CI另待。见[实机部分删除与恢复证据](WORKSPACE_RETIREMENT_PARTIAL_DELETION.md)。

接续：2026-11-11，356ba6c1新QA owner A禁用（组密码不变）期间由B独立UI新键恢复成功；A me在B成功后仍403，身份源核对inactive/原组后才恢复active，原A上下文随后200，不重新登录。两空间产品退役、预算复用、报告及三个账号收尾首次通过；不推定全部票据销毁，见[实机禁用接管证据](WORKSPACE_RETIREMENT_DISABLED_OWNER_TAKEOVER.md)。

接续：2026-11-11，接管验收窗口指针修复经真实本地中止与显式同意图重试通过：同三个账号/空间/原失败task，旧窗口/B退出证明保留、当前窗口2，原FD/run/import证据保持，只一次产品恢复；两空间产品退役及账号/会话收尾通过。703a68f1准确CI全13，新修复自身CI另待；见[实际重试及边界](WORKSPACE_RETIREMENT_TAKEOVER_RETRY.md)。

接续：2026-11-11，356ba6c1实机新QA的owner A移组且me非管理员期间，由B独立登录在自身上下文UI新键恢复成功；原失败与owner保持，身份源及SQL复核成功后才恢复A原组，两空间及三账号收尾首次通过。只签此首轮组合，异常后显式重试的QA窗口指针缺口已保留未签；见[实际证据及剩余门槛](WORKSPACE_RETIREMENT_REVOKED_OWNER_TAKEOVER.md)。

接续：2026-11-11，356ba6c1实机由另一管理员B在独立可见登录上下文UI确认恢复，原owner A及原失败证明/计划/时间保持；B正常退出不影响A原会话，A继续预算复用。独立SQL确认新执行/恢复/审计归属B、原owner/原job归属A，两空间产品退役及三个账号收尾首次通过。原owner保持有效，撤权同时接管组合未签；见[实际证据及边界](WORKSPACE_RETIREMENT_ADMINISTRATOR.md)。

接续：2026-11-11，356ba6c1实机新QA存活execute在有界冻结窗口经真实UI取消202及签名Cancel结算Stopped，保留预算；页面独立新键恢复并清取消标志，原证明/计划/时间保持，两空间产品归档退役/预算复用/8报告及账号/冻结窗口收尾首次通过。验证器自身CI另待；删除已开始后的故障、另一管理员及完整门槛继续。见[Stopped实机证据及边界](WORKSPACE_RETIREMENT_STOP.md)。

接续：2026-11-11，356ba6c1实机新QA完成真实存活执行精确终止→Interrupted保留预算→UI独立新键恢复→两空间产品归档退役/预算复用/8报告与账号收尾，原失败证明/计划/时间保持，首次wrapper通过。892e7958准确CI全13成功，本验证器自身提交CI另待；删除已开始后的强杀、真实Stopped及另一管理员仍待。见[本次实机证据和边界](WORKSPACE_RETIREMENT_INTERRUPTION.md)。原组撤权和账号禁用证据另见对应文档，以下保留原批次事实。

后续：已部署356ba6c1上新增真实新QA owner组撤权验证：同一登录恢复/历史403、原组恢复后独立UI确认接续、原失败证明及预算保持、两空间退役与收尾通过。最终物理计费+4096字节误拒经同证据只读复验闭合，原失败保留；见[真实恢复撤权与精确边界](WORKSPACE_RETIREMENT_AUTHORITY.md)。真实Interrupted/Stopped和其余撤权矩阵仍待。

## 2026-11-11 标准发布及真实用户闭环

产品提交 `9df7f108ab5aaed1768bf250afe8823a5f3dbf51` 的准确 CI
[42929236526](https://github.com/leng125172-code/whale-deck/actions/runs/42929236526) 全 **13/13 success**。
经固定维护入口执行一次标准 dev 更新，更新返回 0；六组件及23个宿主模块同源、11核心健康。
原列投影逐行摘要及表结构核对通过：仅增加声明的派生字段与空恢复表，原两条退役历史的 execute 指针准确回填，旧计划、证明、时间和业务记录保持。

真实 Authentik/Vue/BFF/Worker/Agent 在新专属 QA 空间完成：

- 登录、空间登记、硬配额分配、源码上传/发布、Node 测试 **2/2**、.NET 独立构建与测试 **2/2**，零失败/跳过；含停用后重新启用空间的 Node 复验共4次运行，全部成功。
- 停用、HDD Capture 及独立 Verify 后，通过真实页面核对退役计划。仅对本批新空间的已核对源码保留一个只读 FD；删除后 inode 无链接而仍计费，固定 helper 实际返回 `WORKSPACE_STORAGE_RETIREMENT_QUOTA_STILL_REFERENCED`。原执行终结为 `Rejected`，workflow 保留 Attention、隔离及预算，没有 completion。
- 关闭自有 FD，独立重新勾选继续删除，从页面提交恢复；创建新的 task/attempt 1，接续原 engine journal。旧 task 的请求与证明摘要、派发/结算时间，以及原计划摘要、有效期和首次确认时间由独立 SQL 前后核对保持，原失败及新成功在 UI/历史 API 同时可读。
- 两空间均经支持的归档/显式退役流程收尾：源目录、claim 和 detached source 缺席，内核配额八项全零。相同1000毫核/2048MiB内存/8192MiB磁盘预算再次登记、分配并退役通过；HDD树/inode/摘要保持，4次运行的8份双格式报告在退役后仍可下载并核对摘要。
- 原7条登记（5个活动分配、2条既有退役）及 **18670** 条发布后原记录、源码/配额/预算、旧HDD/归档journal均保持。120,000,000,000/300,000,000,000字节硬限制及已授权 user NOPASSWD 保持；活动任务/锁归零，FD进程正常退出，两名临时身份删除，双方正常退出。
- 320暗色及1440亮色真实页面无横向溢出；已人工查看320暗色执行历史图，原拒绝、新成功、接续ID与摘要清楚。普通账号读取尝试历史和提交恢复均403，独立恢复同意 false 返回400。

证据目录 `deploy/artifacts/workspace-retirement-recovery-delivery-9df7f108-20261111/` 已封存：
**115文件 / 29,129,557字节**（不含manifest自身），manifest SHA256
`9b4dde8cf0aa5ac22a4ed5de6fe8b1f4c33f6706fe6ccbb9456716e9e17145e2`，逐文件复验通过。
重点收据为 `batch-acceptance.json`、`standard-release/migration-backfill.json`、
`standard-release/deployed-but-continuity-failed.json`、`real-recovery/accepted.json`、
`real-recovery/persistent-original-failure-preserved.json` 和 `resource-closure.json`。

### 本轮失败与未通过边界

- 标准更新1728个HTTP样本为1590×200、135×维护HTML503、2×429及1次3秒 `TimeoutError`；**升级连续性未通过**，原样本和失败脚本保留，未重发更新或回滚。429恰为首60秒窗口结束前的第121/122次系统状态请求，符合120/min限流；观察频率需降至1.5秒。超时发生在切换附近，客户端3秒小于代理5秒活动时限；确切上游原因仍未证明。下一次真实更新须记录延迟与异常原因、区分连接/响应预算并重验，不追认本轮通过，不削弱限流。
- 首轮故障验证器向固定只读文件访问器重复传入其自行添加的 `O_CLOEXEC`，被 `FILES_OPEN_OPTIONS_INVALID` 拒绝；发生于确认删除前。保留原失败与独立复现，修正为访问器允许的只读参数后，在同一账号生命周期内续用原空间、归档和退役计划成功，未新建空间掩盖失败。
- GitHub一次CI查询EOF及发布脚本离线语法修正不计产品验收；准确最终CI收据另存，未据传输失败进行发布。
- 本轮真实恢复覆盖 **Rejected**，不宣称真实身份下的 Interrupted/Stopped、恢复专属撤权矩阵或全部P3-18通过。记录/验证器新提交仍须自身准确CI及后续标准发布；其余59项、ST272/PET6/供应商Live2D16继续，完整主任务仍 **0/59**，17项移出不恢复、不计完成。项目图谱待同步。

## 候选阶段原始记录

以下保留发布前边界：2026-11-11，P3-18 中断恢复候选尚未部署，实机为 `d0fe2e02`。当前部署与本轮实际通过项以上方记录为准。

## 行为与约束

- `AttentionRequired` 继续隔离并占用预算。普通重新授权和刷新不创建恢复，原失败任务、回执、原计划及首次确认时间均保留。
- 新增 BFF `recover` 和 `attempts`。恢复要求当前资源授权、批准根、CSRF/Origin、幂等键、当前失败 task ID、准确原计划摘要和独立的继续删除确认。只有原执行已结束为 `Interrupted`、`Stopped` 或 `Rejected`，且没有租约或成功结果时才可创建新尝试。
- 每次恢复创建新的 execute step/task ID 和不可改删的恢复账本，绑定上一 task、原证明摘要、当前身份及确认时间。同键同请求只回放；不同键不能跳过当前尝试，旧回执/旧租约无法结算新尝试。
- 新 task 的宿主请求摘要不同；engine request 保持原 retirement、archive、epoch、计划及存储绑定，接续原持久删除日志。宿主继续独立验证 HDD 和剩余源码，不覆盖或重启原 task。原计划有效期及首次确认不延长，恢复授权另记时间。
- 恢复尝试在派发前取消也不会恢复 `Stopped` 或释放预算，须通过新 task 的宿主 Cancel 墓碑结束，重新进入待处理。撤权、超时和丢响应仍沿用原派发/观察围栏。
- 只有最新成功 task 的完整物理回执与不可变 completion 配对才释放应用容量；旧成功强校验全部保留。步骤、恢复授权和 completion 均有数据库保护，含恢复历史时拒绝降级迁移。
- EF migration 为旧 execute step 回填派生指针，事务内持表锁，仅暂停对应不可变更新 trigger，立即核对原 deferred 约束后恢复，再检查全部新配对；不改旧计划、证明或时间。
- Vue 另行勾选接续删除；丢响应重连原键/原失败 task，刷新/重载读取持久当前尝试。执行尝试列表保留原失败摘要及新 task；401/403 清理同意和详情、停止轮询。

## 本地及隔离验证

证据目录：`deploy/artifacts/workspace-retirement-recovery-candidate-20261111/`。

- 完整 .NET 单元：6277 通过，0 失败/跳过；普通集成：837 通过，7 专用环境测试跳过，跳过不计通过。
- 独立 PostgreSQL 18.6：1 项实际执行、1 通过、0 跳过；由非 superuser 执行真实 migration、事务、约束、BFF/CSRF/Origin/HTTP 和 dispatcher。新增两个 phase 标记均实到，并加入 CI/标准原生验证器强制门禁。
- 事务场景覆盖旧已完成历史升级回填、伪造恢复证明拒绝、审计失败回滚、同键三并发只建一次、不同请求键冲突、旧 task 成功回执隔离、恢复派发前取消仍隔离、再次恢复、换租约只观察同 task、一次预算释放及不可改删原历史。
- HTTP 场景覆盖无 CSRF/幂等键、未同意、错旧 task、跨 Origin、当前管理员撤权、外人历史拒绝；另一当前管理员显式恢复、Start 丢回复不重复派发、原失败及新成功历史读取。宿主回执和身份目录是协议夹具，**不代表实机删除或真实身份验收**。
- 前端完整单测 1496 通过；最终恢复专项 15 项另验。类型检查、生产构建、相关 lint/格式通过。生产包 Chrome 157 的退役/恢复共 8 场景通过，覆盖 320/1440、明暗、独立同意、丢回复重连、重载及历史；历史排版调整后四个恢复场景另验。所有浏览器响应是夹具，不计实际依赖通过。
- PostgreSQL 数据、临时凭据仅在隔离容器/进程中使用，未接入生产网络或数据卷；容器和内部网络已删除。

## 失败保留及交付门槛

- EF 生成迁移最初触发命名空间及数组分析告警，已按现有规则修正，未关闭分析器。
- 隔离夹具先因 Docker local 日志压缩不支持 `max-file=1` 失败，再因 internal 网络不发布端口而停止。均未执行测试；已改为两份有界日志、内部网桥直连和创建失败后的精确资源清理。首轮遗留的 Created 容器已按确切 ID 删除，纠正收据保留。
- 默认 Playwright headless shell 不在本机，首轮八场景未启动浏览器；改用已安装 Chrome 157 后通过，首次失败上下文保留。
- 前序记录提交 `91f3d93d` 的准确 CI `42926114583` 已 **13/13 success**。本候选仍须自己的准确 CI 全绿后，标准 dev 部署、新专属 QA 空间真实身份/中断/恢复/报告/配额及预算验收；不能用本批协议夹具或既有正常退役替代。
- 保留原五活动空间、既有两条真实退役历史及全部旧审计/失败证据；本批未变更实机业务数据、120GB/300GB 限制或已授权 NOPASSWD 配置。用户原有 `deploy/host/whaledeck-development-source.py` 保持 SHA256 `1b79e42155e6e894c80e16bfc537a1c5cce50c718d6653d3b761df1348de20d6`，排除提交。图谱本地 pending，**待同步**。
