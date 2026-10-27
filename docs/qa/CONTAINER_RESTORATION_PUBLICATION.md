# 容器历史恢复：发布准备中断与真实管理员接管

## 当前批次与已知失败（尚未签完整实机整链）

2026-10-27。62fd7d5已正常覆盖，4分24.335秒，六组件/HTTP同源，宿主权限/UDS/动态入口通过。接管页面改为“容器文件写入”，覆盖普通上传、编辑及历史恢复，不再把恢复任务误称上传；提示明确封存不重做任何写入。496前端单元/lint/生产构建、41接管浏览器夹具通过。

冻结观察器868f879在实际Root schema4 PublishPreparing、覆盖前备份已Verified且目标目录私有副本已有非零未完成字节时，对固定Worker SIGKILL/显式启动。自然原租约到期后SQL Unknown保留RootStarted和全局发布屏障；原文件/属性不变，另一独占容器的普通上传保持Ready并实际延后两次。源历史134217735字节，当前原件2097159字节，均为明确自有fixture，HDD来源与完整原件已独立读过。

首轮UI门禁把实际“暂不处置”误写“取消”，未发送确认；失败保留，产品保护未解除。旧Ready计划仍占用接管预约，首次新身份重试被正常拒绝；等其真实Expired后，新的真实管理员正常页面计划/二次确认已封存原schema4并将原任务失败对账，屏障解除。门禁随后因弹窗关闭控件的本地化名称失败，正常退出未签收；两个fixture在精确已知inode/完整字节及私有目录证明匹配后清理，永久Root/SQL/HDD历史保留。等待任务的容器因此已不存在，它在旧版按临时故障等待，尚需到期后的正常封存；不得把此轮称为“等待任务成功发布”或整个门禁通过。

现场 `deploy/artifacts/container-restoration-publication-73befe7042f04e2b911352dab4421be0/`，含baseline、interrupted-root、实际Ready/Disposed截图、首次/恢复两轮stdout/stderr。日志 `/tmp/wd-restoration-publication-live.log`、`/tmp/wd-restoration-publication-resume.log`、`/tmp/wd-restoration-publication-resume-2.log`。观察器已按实际“暂不处置”及弹窗自身关闭控件修正，并在手机展开侧栏后正常退出；处置后先保存fullSsoLogout=false的部分证据，只有实际退出通过才改true，避免确认事实随后续观测失败丢失。

## 已发现的实际产品问题与源码修复候选

容器消失时，固定Docker精确ID查询返回404，原ContainerRoot直接透出DockerFailure，写传输只返回通用UNAVAILABLE，Worker会把确定的目标消失推迟到15分钟到期。候选只对受信DockerFailure且精确inspect响应404产生CONTAINER_FILES_TARGET_MISSING；其他HTTP/缺Socket/缺HDD/形似404异常仍保留原分类。读/写/历史gRPC以FailedPrecondition传递，SDK映射为既有TargetUnavailable业务异常，Worker保留Unknown及证据，不冒充缺少暂存、取消或成功，不自动重发。

冻结旧62fd7d5上两条实际构造/重新验证回归失败，修后256项容器Linux-root通过；Release零警告错误，2720 Unit/199 Integration通过（7真实依赖默认跳过）；任务范围8检查通过。日志 `/tmp/wd-missing-container-before.log`、`/tmp/wd-missing-container-root.log`、`/tmp/wd-missing-container-build-2.log`、`/tmp/wd-missing-container-unit.log`、`/tmp/wd-missing-container-integration.log`。尚未部署新分类，也未据此签实际404业务成功。

## 继续验收

先正常封存首轮剩余测试任务，保留失败记录；覆盖新候选后，用新独占fixture重新执行发布准备中断、跨应用等待、原身份删除后不同管理员的真实UI计划/取消/二次确认/封存、保护解除后的原等待任务成功发布、正常退出及独立清理。交换前后更晚故障、完整属性、容器写入CRUD/权限、长期清理及其余59项仍active，17项移出不算完成。
