# 容器新建目录：持久业务、页面与安装态验收

## 当前状态

2026-10-28最新：Preparing目标消失、Prepared非空对象、原身份删除后另一管理员真实手机处置/跨应用等待，以及Prepared期间产品主动取消/正常退出均已实机通过。Root固定helper迟到Apply仍Unknown，证据和对象不变；最终20任务12成功/3取消/1中断/4Acknowledged，活动/Unknown/租约/屏障0、11核心healthy。产品仍5f3622d，没有重复构建部署。[故障证据及明确剩余](CONTAINER_DIRECTORY_UNKNOWN.md)。

2026-10-28增量：已安装5f3622d的目录排队Worker/Agent真实重启、排队取消与正常退出撤销四项通过，完整SQL/Root/目录inode与准确清理有独立复核。只签执行前排队范围，Root发布准备中断与Unknown跨管理员页面仍待。[重启证据与初次观察失败](CONTAINER_DIRECTORY_RESTARTS.md)。

2026-10-28，永久业务及 Vue 源码 `4341167` 已实现，`5f3622d` 已正常 dev 覆盖并通过真实 Authentik 身份的四组目录页面验收。新增 EF 迁移 `20261027153646_ContainerDirectoryIntents`、独立目录任务/Job/Outbox/事务审计、共享发布保护、Worker、管理员 API、页面计划确认/原登记找回与当前管理员 Unknown 处置。目录业务的实际重启/进行中取消/Unknown 跨管理员页面故障组合仍待，不把底层 SIGKILL 或合成页面证据替代它们。

当前六组件实际程序集、镜像及 HTTP 对应 `5f3622d07f7e265762785be56712323fde1b5405`，Agent 协议仍 minor44。宿主运行 PID 的 `/proc/<pid>/exe --build-info` 也已独立核对。下方 `82850b2` Agent 引擎证据属于前一子阶段，当时没有目录业务 schema/API/UI。

前序 `f7a7731` 内部引擎、`b43fedf` 冻结原生验证已完成真实独占Docker容器普通创建及四个强杀切点；其证据仍保留，下方说明分开标识引擎门禁和新安装态协议门禁。

这是 P1-11 / P2-03 中的容器目录创建增量，不是完整文件管理或59项完成；17项移出不算完成。文件复制、移动、删除、权限和所有者管理继续保留在原目标。

## 永久业务与页面候选

- 五分钟业务确认计划绑定真实主体、原登录、完整容器实例、路径、父版本、普通权限及内核派生所有者。十五分钟永久意图冻结首次登记，Job/Outbox/审计同事务提交，同一登记重试不延长寿命或重做目录。业务 HMAC 与 Root 计划域独立。
- 数据库共享 advisory gate、发布屏障及三条数据库触发器保护旧版滚动写入。宿主文件锁、容器上传及新目录发布不能交叉运行；Unknown 保留保护，普通 Job 重试明确拒绝此任务类型。
- Worker 先查询永久 Root 回执，执行前再次复核原登录/当前管理权/取消/有效期；RootStarted 后不再次 Apply，只查询或封存。当前管理员处置保留原签名、Root 证据及当前处置审计，以失败结束历史任务，不能假报目录创建成功。
- Vue 从实际容器目录新建单层目录，先计划后明确确认；关闭未确认窗口不登记。响应丢失保留同一登记，刷新仅查询原任务；退出、降权和换主体清空本地提示。文件页有独立 Unknown 调查与二次确认，确认回包丢失可以只读查询原处置。

本地 Release 零警告/错误、2802 Unit、201 Integration 通过（7项默认原生依赖跳过）；前端522单元、类型/生产构建、lint/format通过。9项生产预览浏览器测试通过，包含320/1440、明暗、取消零登记、登记响应丢失、原任务取消、当前管理员处置响应丢失与401/403清空、普通用户无管理请求；这是合成接口测试。

冻结 `7c4df06` 在独立真实 PostgreSQL 数据库执行1/通过1/跳过0，准确临时容器/数据库清理通过。目录子场景验证双连接幂等/租约、跨类发布互斥和旧版原始 SQL 触发器、Unknown/跨管理员事务处置及审计失败原子性。没有改生产数据库来制造这些场景。

前两次原生失败保留：旧测试的迁移降级虽然最终被历史保护拒绝，但已逐个提交了更晚空类型的降级，导致后续缺表；测试在验证原保护后恢复最新 schema。审计失败注入的 CHECK 初次扫描到合法历史审计，改为 NOT VALID，仅拒绝新写入而保留旧事实。均修正测试，不绕过产品约束。

日志 `/tmp/wd-directory-business-build-final.log`、`/tmp/wd-directory-{unit,integration}-final.log`、`/tmp/wd-directory-web-{unit,build}-final.log`、`/tmp/wd-directory-browser-final-10.log`、`/tmp/wd-directory-native-postgres-{2,3}.log`；真实 PostgreSQL 状态 `deploy/artifacts/native-status/dispatch-8a490556905f44ebabbd083d4fc501d1.json`。50项容器文件联合页面通过；补权限选择控件后目录10项通过。CI40611513488最终backend/frontend/native-postgres/native-valkey/installer五作业全部success；新增QA提交的后续CI另行跟踪。

## 实际覆盖与人类身份页面签收

正常开发更新服务 InvocationID `3d27ab362ee146f8a54ec35830b398c9` 成功终态0/SUCCESS，用时3分35.989秒；新迁移入库，6组件/HTTP来源及宿主PID执行文件对应5f3622d。宿主检查通过10.0.13/PKCS、UDS/helper权限、HDD策略及动态入口。没有自动回滚。

`deploy/validation/validate_container_directory_browser.py/.mjs` 通过真实正常 Authentik 登录：1440明/暗、320明/暗各新建一个中文目录，普通权限0750/0700/0755。每次取消确认零登记，再明确确认恰好一个业务任务；额外4次相同 HTTP 登记重试返回同任务和原寿命。真实缺CSRF400、错误Origin403，刷新仅找回原任务，浏览器文件内容请求0。两页正常注销后WD/旧Cookie401、Authentik拒绝，普通真实用户8路403与匿名查询401通过。

独立观察SQL永久计划/原主体/目标实例、同Job成功/事件/单条接受审计、Outbox终态、Root永久完整计划指纹与回执、实际空目录inode/1000:1000/普通权限及版本；四项全部匹配。320和1440截图主代理目视检查无水平溢出。精确空目录与私有目录核验后清理，严格fixture证明容器和原测试源准确清理；Root和业务历史保留。新进程再次核对4任务全部成功、接受审计各1、活动/租约/屏障0、Root归档与当前原件逐字节相同、精确CID/源不存在、两类临时身份均0、11核心healthy。

私有证据 `deploy/artifacts/container-directory-browser-c52a54f51ef442379be1d19420d9b0ed/`：baseline/results/verified/independent-final、四份Root永久记录归档、四张实际页面截图和浏览器输出。日志 `/tmp/wd-directory-installed-browser-4.log`、`/tmp/wd-directory-provenance.log`、`/tmp/wd-directory-host-validate-final.log`。

首次发布前置Git TLS失败，未执行安装；临时HTTP/1.1/TLS1.2参数那次拉取成功后进入唯一一次发布。观察时曾准备切换到普通用户，但复核发现该次已调度，未重复发布。sudo确实清除代理，但不把首次TLS错误全归因于它。root Git刷新产生的index/ORIG_HEAD所有者已按准确清单恢复user，只改归属不改内容/模式，Git工作树干净。

页面验收第一次从部署checkout运行缺Playwright依赖，在任何目录登记前失败且准确清理；改从同产品源码且已装依赖的测试工作树运行。非筛选Element Plus Select内部input被选中标签覆盖，原验证器点击超时，真实/本地均复现且目录登记0；改点可见wrapper、不force，补回归10项后通过。保留失败日志，未改产品或放宽权限。

## 实现

- 使用已有 `ContainerRoot`：精确容器 ID、启动实例、PID/root FD、命名空间、Docker 配置指纹、核心资源与共享挂载检查；不接收宿主路径、任意命令或 Docker Exec。
- 只创建单层、原本不存在的目标；中文路径使用现有规范化校验。拒绝特殊模式位、私有/敏感/虚拟路径、符号链接父路径、只读根/挂载及挂载根。实际 `RENAME_NOREPLACE` 保证不会覆盖并发出现的目标。
- 独立版本1计划指纹绑定容器实例、目标证明、父目录 inode/版本、目标路径、普通模式位和从容器实际 PID 取得的内核文件系统 UID/GID。前端不提供所有者。
- 根权限私有状态保存 operation/owner/完整计划、Preparing/Prepared/Applying 与终态、目标和私有目录 inode/属性证据。目录先在根权限私有目录中完成设置与持久化，再原子发布。发布前重新解析父目录并核对私有目录和待发布对象。
- 重放只观察既有回执；取消在执行前写入永久 Aborted 拒绝标记。发布后不回滚、不重复创建。中断只删除已记录 inode 且未被改变的空私有目录；非空、外部替换、外部属性变化或无法证明的丢失都保留 Unknown。没有递归清理或删除已发布目录。
- 目标之前不存在，因此没有伪造旧版本备份；未来覆盖、递归操作和删除仍须各自的 HDD 备份/回收规则。

## 自动化与真实依赖

23项新增真实 Linux-root 文件系统测试通过，包含实际 fork/SIGKILL、发布前/后恢复、重复请求/永久取消、旧父版本、外部文件/目录/链接冲突、私有对象替换/权限变化、非空对象保留、严格计划字段、操作锁、核心读取基础的实例与挂载策略。整个 `test_container_file*.py` 279项通过，零跳过；开启 ResourceWarning 错误模式。范围记账8项通过，只证明既定59项记账未变化。

真实验证使用现有严格 owned fixture：固定已有镜像、非root `1000:1000`、只读容器根、`/work` 唯一可写 bind、无网络/端口、cap-drop、no-new-privileges、资源与日志上限。冻结模块复制到根所有临时目录执行，未修改已安装组件。

| 真实切点 | 持久结果 | 原子发布和恢复证据 |
| --- | --- | --- |
| 正常创建 | Completed | 中文目录、UID/GID 1000:1000、0750、inode/版本匹配；重放不再创建 |
| Preparing已落盘后SIGKILL | Aborted | 没有发布；重放保持拒绝 |
| Prepared已落盘后SIGKILL | Aborted | 只清理原inode的空私有目录，没有公开目录 |
| Applying已落盘、rename之前SIGKILL | Aborted | 只观察和清理私有对象，没有补做rename |
| rename成功、完成回执之前SIGKILL | Completed | 实际目标完整inode/属性与回执匹配；观察后重放不再写入 |

真实只读根与挂载根拒绝、核心容器入口 PermissionError/CONTAINER_PROTECTED 均通过。准确测试容器和源目录由原fixture核验清理；独立进程重新读取五份Root永久回执/计划指纹、Docker精确CID不存在、测试源前缀无剩余，并确认11核心全部healthy。临时根状态证据保留。

私有证据：`deploy/artifacts/container-directory-engine-8e240a5ee4f04fe7a98ad614dee63a87/verified.json`、`independent-final.json` 与 `root-state/`。日志 `/tmp/wd-container-directory-engine-tests-3.log`、`/tmp/wd-container-directories-all-root.log`、`/tmp/wd-container-directory-native-3.log`。

## 新安装态：独立签名协议与固定权限入口

- `whaledeck.agent.v1.ContainerDirectoryService` 提供 Plan/Apply/Inspect/Discard 四个独立一元方法；专用请求摘要和capability域绑定完整方法、目标/模式/所有者派生值、父目录证明、operation/owner和原主体/trace/job/幂等上下文。旧方法签名、上传/历史域、请求篡改和nonce重放均拒绝。
- 根权限固定driver只接受这四类动作和私有stdin；模块固定/root所有/不可写，有界输入、精确EOF/退出、20秒操作期限，独立systemd沙箱35秒/128MiB/50%CPU/64FD/64KiB文件上限。仅写固定目录状态与应用区域，无任意脚本、TCP或Docker Exec入口；停止请求绑定准确InvocationID。
- 安装器创建root:root0700状态及root:root0750模块，宿主检查同步；沿用受限helper sudo规则，没有扩大产品Agent授权，也没有修改用户既有sudo配置。
- SDK要求major1/minor44及目录能力，严格校验目标和终态。只有准确CONTAINER_DIRECTORY_NOT_FOUND代表根回执不存在；Docker/HDD/socket故障不能被当成没有执行。Inspect/Discard在明确容器消失、重启或被保护后仍能核对历史回执/写执行前取消标记，不进入新的目标命名空间。

Release零警告错误；2784 Unit通过，199 Integration通过（7个默认原生依赖测试明确跳过），287容器Linux-root和12宿主运行库门禁通过。首次全单测的1个旧minor43断言已修并完整重跑。代码含64项新增.NET测试与8项传输测试，覆盖SDK、签名、字段篡改、固定进程参数、回执约束和错误边界。

实际宿主组件构建/安装成功，安装器完成服务重启，host validate通过10.0.13/PKCS、UDS/helper/权限/HDD策略及动态入口。最初更新unit因root systemd环境未信任当前仓库而在构建前失败；改为仅该unit的准确safe.directory后安装和validate均成功，随后探针脚本无执行位导致该整unit终态126。没有重复安装或回滚：以bash执行剩余探针发布，独立unit `whaledeck-directory-probe-publish.service` 为0/SUCCESS。不能把前一个整unit写为成功。

冻结82850b2探针通过实际UDS/Agent/helper：中文目录UID/GID1000:1000/0750、Plan/Apply/Inspect、同操作新签名重放不再创建、执行前Discard拒绝迟到Apply；无签名/错误域/篡改模式及UID/nonce重放拒绝、特殊位拒绝、核心和真实只读根拒绝均通过。独立Root永久计划指纹、完整回执、目标inode与返回版本核对，准确空目录清理，原严格fixture证明源/容器清理。没有人类BFF身份/页面或Worker故障验收，不与前序引擎SIGKILL混称整业务故障通过。

独立进程重核：两份Root回执与私有归档一致，已安装模块与冻结源码逐字节匹配；准确CID与源不存在，目录临时unit0；实际运行PID的Agent/MaintenanceHost均82850b2，11核心healthy，原容器文件活动/Unknown/屏障均0。永久Root记录保留。证据 `/data/GitRepos/whale-deck/deploy/artifacts/container-directory-agent-98d04e6770954a37ac8cbaf3e2aab282/`（baseline/rpc、两份完整Root回执、verified/independent-final）；日志 `/tmp/wd-directory-agent-native.log`、`/tmp/wd-directory-host-update.log`、`/tmp/wd-directory-host-validate.log`、`/tmp/wd-directory-probe-publish.log`。

## 保留的验证错误

第一次启动传错完整提交号，来源检查在创建测试资源前拒绝。第二次五个物理切点已经运行，但验证脚本错误地捕获并不存在的 `docker.Rejected`，实际核心保护为 PermissionError，因此整轮不记成功；严格fixture已经证明资源准确清理。修正验证器后以完整新提交重新执行并通过。独立核对初次对 Docker 英文报错大小写作了错误假设，改为大小写无关后读回通过；没有产品或资源写入修正。失败日志保留，不冒称首次全绿。

## 下一步实现要求

1. 独立签名四RPC/SDK/固定helper/版本协商已安装并完成原生验证；继续把它接入下述永久业务工作流，不直接公开为绕过业务确认的通用写入口。
2. Application永久目录意图、EF迁移、计划/主体/登录绑定、确认/幂等、审计/Job/Outbox、租约和失败对账。与既有容器文件写入/宿主写入共用发布保护，不能只依赖进程内锁。
3. Worker执行前复核当前权限/原登录、取消与过期；RootStarted后丢回执只Inspect，不重新Apply。Unknown管理员处置须保留原证据，并挡住迟到请求；容器删除不等于目录操作从未发生。
4. 管理员API及Vue入口：选中容器当前目录、新目录名与普通权限、实际计划与二次确认、异步状态/刷新找回、退出与降权清空。不先放一个未接业务的按钮。
5. 冻结真实PostgreSQL、实际BFF/UDS、普通/匿名拒绝、取消/撤权/并发/发布故障、桌面/手机明暗和正常双端退出验收后，再按正常dev流程覆盖部署。当前运行时无此入口。
