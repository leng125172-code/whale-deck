# 容器历史恢复：真实进程重启与能力重新协商

## 候选修复，尚未覆盖部署

2026-10-27。发布前新增完整属性恢复能力检查：Agent可能在Stage后重启或降级，必须仍具备minor43/属性、历史来源、取消栅栏和永久接管能力，才能申请发布屏障并签发Publish；不支持时保留Ready等待，不重读HDD、不重新Stage。回归用例先在旧实现失败（应Ready，实际Succeeded），修后通过；恢复能力后新Dispatcher继续同一意图，只发布一次。Release零警告错误，2718 Unit与199 Integration通过（7原生默认跳过）。日志 `/tmp/wd-restoration-prepublish-before.log`、`/tmp/wd-restoration-prepublish-after.log`、`/tmp/wd-restoration-restarts-{build,unit,integration}.log`。真实新修复部署及重启整组仍待。

## 首轮实际结果与保留的失败

冻结观察器7b21b7f验证实际安装d8822c6。排队属性恢复在真实停止/启动Worker、重启Agent后成功，原计划/SQL策略及完整134217735字节/4属性/HDD源核对通过；维护入口PID不变。首轮进行中中断使用Docker kill接口，Docker将其作为手动停止而不按unless-stopped自动重启，80秒观察超时；不是产品恢复成功。已正常启动同一Worker恢复健康，未替换镜像或回滚发布。固定原测试容器移除、无法继续当前命名空间观察，Root保留schema4 Aborted、SQL到期进入Unknown，未发生发布、屏障false、目标文件和来源均保留。

原临时身份已删除后，使用另一真实管理员正常登录、计划和确认执行永久接管，schema3封存完整原schema4及摘要，原意图以CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED失败对账；没有删除任务/审计/Root/HDD历史。独立核对确切不存在CID、目标全字节及4属性、私有原inode与完整134217735字节HDD来源后，只清理该已证明的测试源。证据 `deploy/artifacts/container-restoration-restart-b8552b3d8f984573a49227239296d819/`，含失败基线、queued与failed-probe-recovered；日志 `/tmp/wd-container-restoration-restarts.log`、`/tmp/wd-restoration-failed-probe-recovery.log`。这验证了真实schema4异管理员永久封存，但不能替代进行中重启门禁。

观察器已改为打开且重新核对固定Worker的pidfd，再SIGKILL实际进程以模拟进程崩溃；避免Docker的手动停止语义及PID复用。失败清理先恢复固定Worker并请求正常取消，等待事实后才退出独占fixture。旧fixture默认仍要求全部核心CID/image/status/StartedAt精确一致，仅本门禁显式允许同CID同镜像Worker的StartedAt变化，不允许重建资源。后续真实三组门禁结果须另记。

## 后续要求

冻结新版本并覆盖，执行排队重启、实际Root partial后的Worker进程崩溃和Agent重启三组；核对不可变计划/来源/属性策略、一次发布或明确失败、目标/完整HDD来源、维护PID与实际HTTP、精确清理和角色。更晚的发布阶段交换/故障、ACL/xattr、重建CID和长期清理仍属于后续，59项active、17项移出不算完成。
