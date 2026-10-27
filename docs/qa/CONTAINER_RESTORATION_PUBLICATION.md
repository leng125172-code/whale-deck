# 容器历史恢复：发布准备中断与真实管理员接管

## 当前安装态852ca6f：发布准备故障与目标消失整链通过

2026-10-27，正常dev覆盖 `whaledeck-dev-missing-target.service`，0/SUCCESS，3分18.741秒。实际六组件与HTTP均为 `852ca6fe27aa869896ce1e11300bc9ba02764564`，minor43/运行库与PKCS10.0.13、权限/UDS/固定helper/HDD配置/动态入口检查通过。没有schema或线协议字段变化，没有自动回滚。

### 发布准备中断与跨应用保护

冻结观察器d50fd1c，真实BFF历史来源134217735字节，当前目标2097159字节且属性不同。目标覆盖前HDD备份已经Verified；Root schema4 PublishPreparing、私有副本已有8617984字节且仍未完成，才SIGKILL并显式启动固定Worker。没有修改SQL、时钟、缓存、journal或安装测试hook。自然原租约到期后，原任务Unknown保留RootStarted与全局发布屏障，目标全内容/4属性与原inode不变；另一个真实容器的普通上传保持Ready并观察到两次延后。

原管理员正常退出并删除后，另一真实管理员在320暗色正常密码登录，从异常任务页面选择“容器文件写入”、生成一次计划，先“暂不处置”取消确认（确认请求0），再二次确认一次。Root schema3完整封存原schema4 PublishInterrupted及摘要，原任务以CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED失败对账。只在封存完成后，原先等待的另一任务才开始发布并Succeeded；没有重做恢复或从浏览器发送文件内容。原私有副本保留到明确自有fixture清理，实为12615680字节；完整HDD来源/根暂存134217735字节、当前覆盖前备份2097159字节、原文件/属性逐一复核。维护PID不变。

实际Ready/Disposed页面无横向溢出，完成截图已查看；关闭弹窗、按手机导航的实际展开状态打开用户菜单，正常双端退出后API401/Authentik401或403通过。证据 `deploy/artifacts/container-restoration-publication-cb953f3a7c014e179f7ac8ec90d27e6b/`（baseline/interrupted-root/verified/browser-result/independent-final及两幅实际截图），日志 `/tmp/wd-restoration-publication-live-2.log`。

### 实际容器删除被及时识别

普通上传仅生成计划并登记，未发送内容，Worker停止/启动之间由原严格fixture正常核对并移除自己的容器和源。新Worker真实经过Docker404、已安装Root/Agent/SDK，在10.448秒内把原任务置Unknown/CONTAINER_UPLOAD_TARGET_UNAVAILABLE，原15分钟意图尚未到期。接收/发布事件均0、RootStarted空、无发布屏障，也没有伪造“暂存不存在因此取消成功”。另一真实管理员在手机页面以独立计划/二次确认封存Missing历史，原任务失败对账、Root originalRecord=null，正常退出通过。

证据 `deploy/artifacts/container-missing-target-cf88397fe87f4ffcaec0df0385831b05/`，日志 `/tmp/wd-missing-target-live.log`。独立进程再次核对TARGET_UNAVAILABLE事件1、接收0/发布0、schema3空原回执及准确CID/源不存在；不把真实目标缺失和HDD/Socket等缺失混为一类。

### 独立最终核对与门禁

两个成功门禁均有independent-final：79永久意图/56成功/30恢复（属性16），容器文件活动/Unknown/租约/屏障0；三类临时身份计数0、准确测试源/容器均消失、11核心healthy、两宿主服务active。原任务、审计、Root封存及HDD来源保留，未删除用户数据卷。验证源目录与HDD同设备时只证明版本留存，不冒称异盘灾备。

Release零警告错误，2720 Unit/199 Integration通过（7原生默认跳过），256容器Linux-root、496前端单元/lint/生产构建、41接管浏览器及8任务范围检查通过。冻结旧62fd7d5上的两条目标404回归先失败，修后通过。09eb09b CI40518574767后端/安装器/原生PG/原生Valkey success，前端完整回归仍运行；后续验证脚本和文档提交未假称完整CI通过。

### 首轮失败已收尾，不改写历史

首轮两条遗留任务（原恢复及等待普通上传）均已由正常当前管理员计划/确认封存为Failed/CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED，屏障解除，没有删除SQL或改状态。相关恢复stdout/stderr、waiting-recovery/browser-result（退出当时未通过）仍保留。退出观测先后修正了本地化关闭按钮及CSS变换后仍isVisible的手机侧栏；单独手机退出的第一次断言未能唯一定位，保留失败日志，后来同版独立退出及两条完整业务UI退出通过，不将一次重试声称为认证缺陷修复。Source脚本的Python/Node启动错误只算工具失败，不计产品测试成功。

旧记录与当前通过证据分开列出。目录清理等早先已经接SQL/API/UI的项目已据实际qa记录修正任务表中的过时缺口，状态仍部分实现，59项不缩减、17项移出不算完成。后续推进容器目录/复制移动/权限等基本写入、完整留存/清理与其余未实现能力；更晚交换/撤权故障等仍按原范围跟踪。


## 前序批次与已知失败（后续整链另行通过）

2026-10-27。62fd7d5已正常覆盖，4分24.335秒，六组件/HTTP同源，宿主权限/UDS/动态入口通过。接管页面改为“容器文件写入”，覆盖普通上传、编辑及历史恢复，不再把恢复任务误称上传；提示明确封存不重做任何写入。496前端单元/lint/生产构建、41接管浏览器夹具通过。

冻结观察器868f879在实际Root schema4 PublishPreparing、覆盖前备份已Verified且目标目录私有副本已有非零未完成字节时，对固定Worker SIGKILL/显式启动。自然原租约到期后SQL Unknown保留RootStarted和全局发布屏障；原文件/属性不变，另一独占容器的普通上传保持Ready并实际延后两次。源历史134217735字节，当前原件2097159字节，均为明确自有fixture，HDD来源与完整原件已独立读过。

首轮UI门禁把实际“暂不处置”误写“取消”，未发送确认；失败保留，产品保护未解除。旧Ready计划仍占用接管预约，首次新身份重试被正常拒绝；等其真实Expired后，新的真实管理员正常页面计划/二次确认已封存原schema4并将原任务失败对账，屏障解除。门禁随后因弹窗关闭控件的本地化名称失败，正常退出未签收；两个fixture在精确已知inode/完整字节及私有目录证明匹配后清理，永久Root/SQL/HDD历史保留。等待任务的容器因此已不存在，它在旧版按临时故障等待，尚需到期后的正常封存；不得把此轮称为“等待任务成功发布”或整个门禁通过。

现场 `deploy/artifacts/container-restoration-publication-73befe7042f04e2b911352dab4421be0/`，含baseline、interrupted-root、实际Ready/Disposed截图、首次/恢复两轮stdout/stderr。日志 `/tmp/wd-restoration-publication-live.log`、`/tmp/wd-restoration-publication-resume.log`、`/tmp/wd-restoration-publication-resume-2.log`。观察器已按实际“暂不处置”及弹窗自身关闭控件修正，并在手机展开侧栏后正常退出；处置后先保存fullSsoLogout=false的部分证据，只有实际退出通过才改true，避免确认事实随后续观测失败丢失。

## 已发现的实际产品问题与源码修复候选

容器消失时，固定Docker精确ID查询返回404，原ContainerRoot直接透出DockerFailure，写传输只返回通用UNAVAILABLE，Worker会把确定的目标消失推迟到15分钟到期。候选只对受信DockerFailure且精确inspect响应404产生CONTAINER_FILES_TARGET_MISSING；其他HTTP/缺Socket/缺HDD/形似404异常仍保留原分类。读/写/历史gRPC以FailedPrecondition传递，SDK映射为既有TargetUnavailable业务异常，Worker保留Unknown及证据，不冒充缺少暂存、取消或成功，不自动重发。

冻结旧62fd7d5上两条实际构造/重新验证回归失败，修后256项容器Linux-root通过；Release零警告错误，2720 Unit/199 Integration通过（7真实依赖默认跳过）；任务范围8检查通过。日志 `/tmp/wd-missing-container-before.log`、`/tmp/wd-missing-container-root.log`、`/tmp/wd-missing-container-build-2.log`、`/tmp/wd-missing-container-unit.log`、`/tmp/wd-missing-container-integration.log`。尚未部署新分类，也未据此签实际404业务成功。

## 继续验收

先正常封存首轮剩余测试任务，保留失败记录；覆盖新候选后，用新独占fixture重新执行发布准备中断、跨应用等待、原身份删除后不同管理员的真实UI计划/取消/二次确认/封存、保护解除后的原等待任务成功发布、正常退出及独立清理。交换前后更晚故障、完整属性、容器写入CRUD/权限、长期清理及其余59项仍active，17项移出不算完成。
