# 容器普通权限：真实页面中断、进程恢复与Unknown接管

2026-10-30。产品保持已装`cb4c400`/Agent1.47；本轮没有修改或重复部署产品，也没有自动回滚。以下是普通非递归chmod的五类故障子门禁，不等于所有者、递归、ACL、删除或整个权限模块完成。单工作站59项继续，17项移出不算完成。

## 真实执行范围

原管理员正常Authentik表单登录，在真实容器文件页面预检并明确确认0600。先停当前Worker让确认落为Pending，再启动同一Worker/CID/镜像；观察器仅在原执行ID、完整计划指纹、原文件九字段指纹、容器根FD和操作锁FD同时匹配时，通过pidfd暂停对应已安装固定helper。实际Root已写入Preparing/Prepared、HDD元数据已验证，原文件仍未修改，才开始故障动作。

没有替换HTTP响应、注入Cookie、改写SQL/Root/租约/时钟或安装产品测试钩子。SIGSTOP只用于可观测切点；helper由产品取消/断线回收，验证器不向其发送SIGKILL。Worker强杀前排除其他任务；Agent重启另排除终端、路径锁、其他发布屏障和其他活动transient。

| 场景 | 实际动作与结果 | helper退出观察 |
| --- | --- | --- |
| 页面取消 | 在真实手机页面先返回继续等待，再明确请求取消；原意图/执行Job Canceled，Root Aborted | 5.457秒 |
| 正常退出/原登录撤销 | 通过真实用户菜单完成WD与Authentik退出；原意图/执行Job Canceled，Root Aborted | 5.433秒 |
| Worker强杀 | 准确Worker容器SIGKILL后显式启动同CID/镜像；等待原自然租约到期后Failed/Interrupted，Root Aborted | 0.121秒 |
| Agent重启 | systemd真实重启Agent；维护入口PID不变，Failed/Interrupted、Root Aborted | 0.024秒；完整服务drain 30.392秒 |
| 原目标容器消失/Unknown | 只移除准确的临时容器，再由原用户真实页面退出触发产品结束helper；原模式事实null、HDD已验证、保留保护 | 5.494秒 |

退出时钟在故障动作前启动，并发观察原pidfd，遵守绝对15秒窗口；不以较晚的服务drain结束时间代替helper退出时间。Worker场景记录原租约值及三次未到期时的Applying/保护保持，完成时间不早于原租约到期，没有提前接管或改数据库。

四类中断均从实际页面登记/确认各一次，内容PUT为0。取消只发送一次实际取消；其余仍登录的页面查询原任务并显示对应失败/取消终态后正常退出。两宽明暗组合、320暗色取消终态截图已目视复核。每例独立SQL双任务/Outbox/事件及原主体结果审计、Root Aborted/modeConfirmed=false和物理HDD元数据完整摘要对应；2097159字节原文件全量及原inode/UID/GID/mode/mtime/ctime均未改变。

## Unknown与另一管理员的真实手机页面

- 原helper暂停时准确移除本轮无网络、非root临时容器；绑定的合成源继续保留。原管理员通过页面正常退出并结束浏览器，其临时Authentik身份实际删除后，才创建不同管理员。
- SQL保持Unknown、原执行Job Running、modeConfirmed=null、publishedVersion=null、HDD已验证，发布保护保留。原Root自然收敛到永久Unknown，不伪造“已确认未修改”。物理源完整内容和全部原指纹不变。
- 另一容器的真实目录写入已登记，三次观测均Pending/RootStarted为空、目标不存在；nextAttempt实际延后，证明被保护推迟。
- 不同管理员正常登录320暗色文件页，看到真实Unknown及HDD事实；先“暂不处置”（确认请求0），再一次明确“确认处置并释放保护”。显示不代表权限修改成功，原意图Acknowledged/原执行Job Failed，Root仍Unknown，模式事实仍null。独立审计核对确认者为新管理员，原执行主体仍为旧管理员，处置审计唯一。
- 另一容器目录只在处置释放保护后才实际创建；其RootStarted时间不早于原意图CompletedAt。不同管理员随后也正常双端退出。
- 从已安装固定受限helper提交原操作的迟到Apply，仍返回Unknown/replayed，未再次chmod。原Root完整SHA、HDD元数据和物理源不变，不删除永久历史。真实确认和完成手机截图均已目视复核，页面无横向溢出。

上述业务位于HDD绑定源和物理HDD元数据位置，不冒称本批已经验证NVMe来源或内容异盘容灾。只在完全核对后清理每个准确测试容器和合成源；元数据和业务历史继续保留。

## 失败保留与重验

- 最初取消验证器把观察Future的等待设为1秒，实际产品已按其6秒复核结束helper；父命令TimeoutError。改为等待完整绝对15秒观察窗口后，四场景完整命令通过。首轮失败保留；用真实SQL终态释放其私有UI等待，原父命令仍记失败，没有改成成功。
- Unknown第一轮实际原页面、跨管理员手机处置和等待目录都通过，但迟到helper探针stdin少了行协议末尾换行，返回CONTAINER_PERMISSION_UNAVAILABLE，父命令失败。准确清理保留；同一永久Unknown的无换行拒绝/正确换行Unknown另行记录。修复探针帧后，第二轮完整命令及独立只读复核通过；产品未修改。
- `c850360`初始验证器、`369099b`等待与独立核验、`f084c87`Unknown真实页面、`712fb0f`迟到传输帧修复；`a75673a`追加独立原/当前管理员审计核对并移除未使用的主动kill方法。后置审计核对已真实执行通过，未弱化原断言。

## 本轮检查与私有证据

- 新12项Linux-root观察器边界全部执行零跳过，涵盖外来进程/锁/符号链接/旧回执/状态/路径/指纹和pidfd退出时钟；既有26权限+9传输Linux-root、8项范围检查通过。新增Python在`-W error`下编译、JS语法检查通过，已接CI。
- 四类中断完整父命令退出0，独立只读观察器重读原SQL/Root/HDD、摘要、审计和准确资源不存在均通过；Unknown完整父命令退出0，另一只读进程复核跨主体唯一审计/原Root不变/HDD/迟到Apply/等待时序及准确清理通过。
- 当前活动权限任务/租约/屏障和临时验证身份均0，11核心healthy；Worker容器/镜像未换，其他10核心ID/镜像/状态/StartedAt不变。Agent新进程仍为cb4c400，维护入口PID不变；安装态宿主/动态入口校验通过，user NOPASSWD:ALL继续保留。
- 本批只有验证器和文档变化，后端3375 Unit/213 Integration及695前端等前轮构建证据独立保留，未声称本轮重复执行。GitHub CI尚未取得全成功终态，不签全绿。
- 四例：`deploy/artifacts/container-permission-faults-ba389999e8d24a4091dd8539884628e4/`，每例baseline、实际UI、cut-root/bound-helper、final-sql/root/hdd、7张PNG及独立结果/摘要。
- Unknown：`deploy/artifacts/container-permission-unknown-ff681b261f36492e92714f6e60c1e379/`，原/新管理员UI、Unknown/最终SQL、Root/HDD、等待保护/实际目录、迟到Apply、3张PNG及独立结果/摘要。
- 失败：`container-permission-faults-40dc0526cb4c4ea4b717c811a5b05f80`和`container-permission-unknown-b3c916f6097b4e29a393565b673f8dab`，失败父命令、实际结果与准确清理证据保留。主日志`/tmp/wd-permission-faults-all.log`、`/tmp/wd-permission-unknown-second.log`及`*-independent-audit.log`。这些私有证据不入Git。

## 继续实施

先完成所有者变更的独立计划、HDD属性前置留存、强类型RPC/最小授权、永久业务任务和页面，再扩展递归与ACL及其故障/恢复；容器删除需默认可恢复、保护预检、HDD历史、持久任务与真实页面。当前这些能力未签收。

普通权限还保留：实际组撤权/账户停用的执行中边界、chmod之后与回执丢失切点、容器重建映射、跨盘与其余故障组合。本次正常退出仅证明原登录撤销，不替代这些验收。全部59项仍active，master未正式交付。
