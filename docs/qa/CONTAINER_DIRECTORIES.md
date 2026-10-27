# 容器新建目录：内部引擎及真实容器验收

## 当前状态

2026-10-27，`82850b22d3cb7a18ddc4a89af966d60dbc7e1197` 的宿主Agent/MaintenanceHost和固定helper已覆盖并通过安装态签名UDS验证，协议minor44。API/Worker实际程序集与Gateway标签仍为 `852ca6f`，没有公开目录写入API或Vue入口，也没有目录业务SQL/Worker任务。此阶段没有数据库schema改动，不生成空迁移。

前序 `f7a7731` 内部引擎、`b43fedf` 冻结原生验证已完成真实独占Docker容器普通创建及四个强杀切点；其证据仍保留，下方说明分开标识引擎门禁和新安装态协议门禁。

这是 P1-11 / P2-03 中容器目录创建的底层实现，不是完整“新建目录”产品功能验收，更不等于59项完成；17项移出不算完成。文件复制、移动、删除、权限和所有者管理继续保留在原目标。

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
