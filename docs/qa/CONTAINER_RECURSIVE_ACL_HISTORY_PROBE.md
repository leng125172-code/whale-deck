# 递归 ACL 历史安装态探针与验收工具候选

日期：2026-11-04；冻结工具提交 `3f44c233d06bd42c686301efe81ab197cade9ab5`，dev。**本批完成探针和验收工具准备，未安装新Agent，未通过1.54历史List/Read实机验收、业务页面或恢复写入。** 当前59项完整完成数不增加。

## 已实现的工具与边界

- 新增独立原生模式 `container-recursive-acl-history`，只走固定 `/run/whaledeck/agent.sock`、完整请求短时签名、独立历史域/方法和固定自有目录；内存密钥最终清零，输出不含密钥、token或原始异常。只允许list/read/quit，不提供apply/discard/restore或任意宿主路径。
- 候选拒绝集是List 21项、Read 19项，覆盖缺签名、普通/递归写域、普通历史域、过期/未来、方法/请求篡改、缺标量、分页范围、核心保护、错误摘要、文件目标、缺失目标和nonce重放。**这40项尚未在新安装态执行。** 普通文件目标由只读transport的目录guard先拒绝，不能误按目录历史不匹配预期NotFound。
- 安装态脚本准备复用原有八个新自有递归ACL场景：七完成/一取消，随后在同一仍活容器上逐页读取七份完整聚合、每份四项前像（含零变更/命名ID0/default清空）。这些是待执行计划，不是本轮新增捕获数量。每次RPC及内部拒绝后核对整个源树、Root/HDD及保护快照；分页版本/摘要、异质ACL和整份计划计数独立核对。
- 逐项读取的协议128KiB与外层JSON转义帧分开限制：探针父级最大1MiB，仅限本探针，不放宽产品驱动上限。拒绝空白/越界/遗漏presence、bool/float冒充整数、额外私有字段、错误捕获/摘要、漏页、重页和伪造完成。
- 独立进程在清理前重新打开真实ContainerRoot和所有文件FD，核对当前namespace、非IDMAP证明、每字节/九字段/两ACL/普通属性，并重开attested HDD核对完整manifest/捕获摘要；清理后的第二检查只核对存档和源缺席，不补签活文件重读。失败不进入源树清理，保留待核验数据；无自动恢复、重放或回滚。
- 修正旧递归ACL独立验证器将protocolMinor写死53的前向兼容问题：接受合法uint32的53及后续minor，保留严格字段、实际宿主/探针来源和能力门禁；bool/字符串/浮点/低版本及版本篡改仍拒绝。新历史入口另检查所有递归业务活动均为0。

## 本轮实际执行

本机Release原生探针构建0警告/0错误；新增12离线证明检查、150递归ACL及83历史Linux-root工具均通过且零跳过，两组重叠不累加；8范围检查和Python语法/diff通过。另启动本机dotnet探针三次，空/未知字段/非法版本头均在读取凭据与RPC前拒绝，错误输出不回显标记。没有修改产品业务/Schema/Vue，本轮未重跑全部后端或前端测试；上一批4744/569（7默认原生跳过）保留原归属。

冻结3f44c23后，使用仓库标准 `publish-terminal-probe.sh`，由固定摘要SDK镜像从git archive构建自包含产物，未挂载含私有环境文件的工作区；60份Python静态依赖闭包已冻结。产物 `deploy/artifacts/terminal-probe.BAyjKG/TerminalProbe` 的完整SHA256SUMS验证通过，原生二进制可在主机直接运行，探针来源检查通过。构建Invocation `e12c04f74178458a87c0a04845c1fd76` 的准确success/ExecMainCode1/ExecMainStatus0及日志在停止前归档，QA构建unit已停止。

在**仍是b880208/1.53的真实安装态**上做了两个有界UDS能力检查：新历史模式在capabilities阶段退出1，ready/history List-Read均未发生；旧递归ACL模式返回真实minor53的ready，然后quit/done正常退出0，Plan/Apply/Inspect/Discard均0。这证明新探针对旧能力拒绝和旧能力握手可达，**不算新历史RPC、旧写入回归、Authentik/资源授权或页面验收**；没有通过模拟1.54服务补签通过。

## 失败与运行态保持

第一次标准构建启动，我直接执行了仓库未设可执行位的脚本，准确Invocation `3ff3fa20bb384631820ee15343a571b4` 退出126，SDK容器/探针产物/系统安装尚未开始。保存原日志、退出状态和journal后，仅停止/reset该QA unit；改用bash调用相同脚本的新unit成功，未更改仓库文件执行位或产品授权。Git推送早期TLS中断和低速超时保留；上轮文档f5eca94及本轮工具3f44c23均已随后正常推送dev。

私有证据目录 `deploy/artifacts/recursive-acl-history-probe-3f44c23-d1527f7d4b4a43f294a6f1a1b5136c77/` 为0700/文件0600，产物目录0700、可执行文件0700/数据0600。前后只读对比591份受保护配置/发布账本及普通/递归ACL Root/HDD全部SHA和九字段保持；11核心容器身份/镜像/StartedAt保持且healthy，Agent4044173/维护3598736及实际b880208来源不变，活动任务/锁/屏障/租约/终端0，永久递归ACL30意图/28执行/58Job及状态分布保持。未安装两份历史模块，不新建工作站源/历史捕获、真实人类身份，不重启Agent/Worker，不修改user NOPASSWD或发布账本。临时SDK构建容器已正常结束。

准确功能提交2b30e09的CI `41820072967` 已确认completed/success；工具3f44c23的CI `41823225450` 仍运行，不称新CI终态通过。下一批按已成功的准确功能版本标准构建/安装宿主，取得真实1.54签名List/Read与独立源/Root/HDD证明，再继续管理员业务/API/Vue、逐inode恢复写入与故障矩阵、可恢复删除/长期清理、维护入口连续性及其余59项。源与逻辑HDD先前同device2049，本轮没有异盘布局/恢复新证据；不自动回滚dev。[协议候选](CONTAINER_RECURSIVE_ACL_HISTORY_RPC.md)、[内核实盘](CONTAINER_RECURSIVE_ACL_HISTORY_ENGINE.md)、[当前目标](../GOAL_P0_P5.md)。

项目图谱事件 `whale-deck.container-acl.3f44c23.recursive-history-probe` 已取得相同project/key/event回执，阶段保持active。私有日志、标准探针产物、源闭包与校验和均保留本机，不上传凭据或内部端点。

收尾链接检查实际698条/缺失0；私有offline-checks初始误录为700，已新增corrected记录及补充校验和更正，原24份封存文件和原SHA256SUMS不覆盖。该转录错误不改变构建、测试或运行态结论。
