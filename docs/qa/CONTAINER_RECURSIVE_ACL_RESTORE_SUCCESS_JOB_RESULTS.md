# 成功恢复任务摘要：真实整链候选与验收

2026-11-05，承接[Ready与执行中退出摘要](CONTAINER_RECURSIVE_ACL_RESTORE_JOB_RESULTS_REAL_UI.md)。本批运行产品仍为ac90911，仅扩展验收工具，不发布产品或回填旧Job。

## 实机分项签收

冻结`eb477a417ff88406de2e70a74d769de0e0097039`，108份验证源码闭包。一次全新自有容器/四对象树先完成8轮真实递归ACL来源操作，每轮独立读取实际文件、SQL、Root和HDD前像后才放行下一轮；7次成功来源/1次确认前取消。然后真实Authentik/Vue进行以下恢复，新增5个规划Job及4个执行Job，未重放任何旧来源操作或旧恢复任务。

| 新恢复场景 | 完成核对 | 本次实际修改 | 来源原修改 | 结果 |
|---|---:|---:|---:|---|
| access真修改 | 4 | 4 | 4 | Succeeded/Completed |
| access无须修改 | 4 | 0 | 4 | Succeeded/Completed |
| 恢复default | 4 | 2 | 2 | Succeeded/Completed |
| 清除default | 4 | 2 | 2 | Succeeded/Completed |
| Ready后确认前取消 | 未执行 | 未执行 | 4 | 意图Canceled，无执行Job |

四次完成的中止/未开始/未知数均0，绑定和历史事实与原生回执一致、发布围栏false；无须修改时全树及原字节不变。五个规划Job的原Ready整行保持，不把取消意图状态写入已成功的预检历史；预检实际计数始终null/“未证明”。正常JobsView take=100列表与详情、26字段白名单、23标签和值及历史提示逐项对应原SQL；18张独立320×600暗色摘要图与原来源/恢复页面共78真PNG，人工检查无须修改计数图。未将这些图片扩大为全部结果状态或全尺寸验收。

原上下文管理员正常BFF/Authentik退出后，Jobs列表和9个详情均401；普通用户同10路403、Jobs页面私有请求0，正常双端退出后同10路401。原恢复4路GET/4路POST普通拒绝、匿名4路拒绝及普通用户无私有审计也保持；没有注入Cookie、伪造接口或绕过正常登录。清理前独立进程重开所有来源/恢复SQL/Root/HDD及完整物理树，通过后才准确清理；清理后另一独立进程再次验证原件、无临时身份/容器/源及活动任务0，不声称重读已删除源字节。

unit `whaledeck-qa-restore-success-jobs-eb477a4`，Invocation`de71a072609e4966859187f920ba7791`。应用中断期间该systemd进程继续运行，没有重新启动门禁；最终MainPID0/SubState=exited/Result=success/ExecMainStatus0，父级与前后两个独立核验均通过。无故障排空或目标保留触发。

私有证据`container-recursive-acl-browser-4053f9fe9f33462380ff113e4680f809`保存326份文件，逐项SHA256通过，清单摘要`1b83365aa20e3e2c340e55a19bc2b079eea290377350ac05f541c4ec4942eba1`。旧候选60、发布27、原退出164、Ready/Canceled摘要170及其首次失败77共5份封存逐项保持。真实恢复聚合4、逐项HDD16保留；旧2060保护原件保持，总2130，来源Root1748/恢复Root240-HDD157/目录Root94。恢复20意图/15执行/活动0，普通ACL62意图/56执行/118Job；11核心容器及两宿主PID、ac90911来源保持，本批未重启Worker/Agent/维护入口，未发布产品或改变安装/权限配置，两个Git索引仍1000:1000/664。

本批源码90项关联root工具测试重新归档通过，零跳过，其中11项新增负向证据校验；该数字与上批96项是不同测试集合，不能视为总覆盖下降或累加总数。此前6cd6fd0的CI41941672023已确认准确head并completed/success；本批eb477a4的CI41945001041单独跟踪，不以旧CI代替。仍需Partial、无回执Unknown、处置及补获回执摘要、其余恢复故障矩阵、可恢复删除/长期清理、维护连续性和其余59项；旧维护连接失败与源/HDD同device限制保留，不提升整项或阶段完成率。

## 首次实机前的候选记录

新增显式`restore_job_results`模式，仅允许完整全新隔离容器与四对象来源。复用原8轮递归ACL页面操作及物理/SQL/Root/HDD证明，再通过Vue执行access真修改、access无须修改、default恢复、default清除四次成功，以及一次Ready后确认前取消。每轮五个Ready规划摘要与四个Succeeded执行摘要均读取真实JobsView take=100列表和详情，完整26字段、23个标签及页面值分别对照原SQL；额外18张320×600暗色截图按轮次/阶段唯一命名，不能覆盖原图片。

每次确认前，父进程取得真正Ready SQL、来源完整Root/HDD及物理保持事实后才放行；执行后沿用原逐inode恢复证明。无须修改必须显示完成核对4、实际修改0，来源原修改4单独保留。四次成功后和Ready取消后，原规划Job整行保持Succeeded/Ready历史；后者不创建执行Job，不能伪造Canceled执行摘要。管理员正常退出、普通用户及其正常退出后的Jobs列表和九个详情分别验证401/403，普通Jobs页面无私有请求。

失败时先保留本批准确容器/物理源，停止仅QA的Node进程并等待原业务结算与helper退出，再释放临时身份；超时也不删除源、不重放确认/取消/ACK。原生Root/HDD和物理证明仍为必要条件，任务摘要不能替代。清理前后独立读取保持既有边界，清理后不称重读已删除源字节。

候选阶段11项新增负向测试、关联共90项root工具测试通过，零跳过；覆盖来源/本次/实际混淆、null当零、Ready历史覆盖、取消时伪造执行、回执/绑定/历史/计数不符、标签错置、截图集合、全部原Job权限拒绝与故障目标保留。10个Python AST、Node语法、CI YAML及diff检查通过。构造证据仅作测试夹具；此处尚未启动新实机门禁，不能签真实整链。

Partial、Unknown、处置与其余故障矩阵、可恢复删除/长期清理、维护连续性及59项仍须推进；源/HDD同device和旧维护连接失败继续保留，不提高整项或阶段完成率。


收尾：范围8项、746本地链接/缺失0、diff检查通过。准确eb477a4的CI41945001041已见后端、原生PostgreSQL、原生Valkey、安装与前端检查成功，浏览器分片仍在运行，未记整条CI成功。项目图谱事件whale-deck.container-acl.eb477a4.restore-success-job-summaries已取得project/key/event匹配且duplicate=false回执，pending移至synced，阶段保持active。
