# 容器历史恢复：真实进程重启与能力重新协商

## 当前安装态：2e13376 已覆盖并通过真实重启门禁

2026-10-27，正常dev覆盖 `whaledeck-dev-2e13376.service`，0/SUCCESS，2分54.329秒；六组件与实际HTTP来源均为 `2e133769ada9bb1bf88707162292a484016c3c38`。实际运行库/PKCS10.0.13、权限、UDS、固定helper、HDD配置和动态入口检查通过。新增发布前能力重新协商已在位，没有新协议或schema，没有自动回滚。

真实安装态完成三种场景；每次历史来源134217735字节，由正常BFF上传产生可信HDD捕获，带不同的历史UID/GID/权限/纳秒mtime与当前文件属性。没有写SQL、缓存、时钟或伪造Root回执：

| 场景 | 实际操作及正面证据 | 结果 |
| --- | --- | --- |
| 排队重启 | 停Worker后登记属性恢复，SQL仍AwaitingUpload、无Root暂存；重启Agent并启动同一Worker | Succeeded；完整134217735字节及4属性/原inode/HDD一致，接收1次、发布1次、完成1次 |
| Worker进行中强制重启 | Root schema4 Receiving且已有65536字节partial，固定Docker接口SIGKILL再显式启动同一CID/镜像 | Failed/CONTAINER_UPLOAD_INTERRUPTED；自然等待原90秒租约到期后对账，Root Discarded、目标原内容和4属性不变、无发布事件/新HDD捕获、partial与data均清理 |
| Agent进行中重启 | Root schema4 Receiving且已有32768字节partial后，systemd正常重启固定Agent | Failed/CONTAINER_UPLOAD_INTERRUPTED；原Worker PID不变，Root Discarded、目标/来源不变、无发布与暂存残留 |

每种场景不可变意图字段（原计划、来源、MAC、大小/SHA、属性策略、时间及原身份绑定）逐一一致，原维护入口PID不变，真实HTTP继续对应同一版本。Worker使用Docker授权接口强制停止/显式启动；**不宣称已验证Docker自动拉起策略**，也没有新增CAP_KILL或绕过宿主权限。

排队场景证据 `deploy/artifacts/container-restoration-restart-c9f3cc108c82430a879c3be5cd411df3/queued.json`；两种进行中场景及完整verified为 `deploy/artifacts/container-restoration-restart-d93223138e1c45bea92ea7b88072db88/`。独立新进程再次核对三条SQL、接收/发布事件数、Root schema4与派生属性、全部134217735字节HDD来源，准确CID和源目录均不存在；两目录各存 `independent-final.json`。过程日志 `/tmp/wd-container-restoration-restarts-2.log` 与 `-3.log`。

## 新版本网页回归与最终状态

进程重启之后，2e13376实际网页1440明色/320暗色再次完成4次属性恢复，覆盖原路径和其他路径的新建/覆盖，每份2097159字节；SQL/Root/HDD/旧inode与4普通属性一致。确认取消零登记、浏览器内容请求0、正常跨页双端退出通过；真实普通用户5路403/匿名401/零登记。证据 `deploy/artifacts/container-restore-browser-15969cd9a7024c00b5b9942e9f29e94d/`（含独立复核），日志 `/tmp/wd-restoration-restarts-final-browser.log`。前端源码本批未改；本条是当前安装态的实际回归，不复用旧版截图冒充新验收。

独立最终核对：72永久意图/53成功/28恢复（属性14），活动/Unknown/租约/屏障0；三类临时身份0，准确测试源/容器均清理，11核心healthy、宿主服务active。任务、审计、Root与HDD历史保留。Release零警告错误、2718 Unit/199 Integration通过（7本机原生默认跳过）；冻结561f599真实PG1执行/1通过/0跳过、临时资源清理通过，状态 `deploy/artifacts/native-status/dispatch-95a601f060504106af4c8cc9adae0e1c.json`。2e13376 CI40509563766后端/原生PG/原生Valkey/安装器success，前端完整回归仍运行；此前d8822c6与2ce27c7已全success。

## 第二轮观察器权限拒绝及正常收尾

pidfd直接发信号被宿主PermissionError拒绝，未增加权限。观察器在原fixture仍存在时通过正常取消路径收尾，实际SQL Canceled、Root Discarded、无RootStarted/发布；精确文件/容器已独立清理确认，证据位于上述c9目录 `permission-refusal-cleanup.json`。未将该失败标成通过；随后只重跑尚未完成的两项，使用授权Docker固定资源接口，显式区分手动重启与自动重启语义。

后续仍需更晚发布阶段交换/故障与进行中撤权、完整属性ACL/xattr/特殊位/atime、重建CID及长期清理等；三种进程场景不等同完整文件功能或总目标完成。59项active，17项移出不算完成。下方为前序候选和第一轮失败记录。

## 历史候选修复（后续已在2e13376覆盖）

2026-10-27。发布前新增完整属性恢复能力检查：Agent可能在Stage后重启或降级，必须仍具备minor43/属性、历史来源、取消栅栏和永久接管能力，才能申请发布屏障并签发Publish；不支持时保留Ready等待，不重读HDD、不重新Stage。回归用例先在旧实现失败（应Ready，实际Succeeded），修后通过；恢复能力后新Dispatcher继续同一意图，只发布一次。Release零警告错误，2718 Unit与199 Integration通过（7原生默认跳过）。日志 `/tmp/wd-restoration-prepublish-before.log`、`/tmp/wd-restoration-prepublish-after.log`、`/tmp/wd-restoration-restarts-{build,unit,integration}.log`。真实新修复部署及重启整组仍待。

## 首轮实际结果与保留的失败

冻结观察器7b21b7f验证实际安装d8822c6。排队属性恢复在真实停止/启动Worker、重启Agent后成功，原计划/SQL策略及完整134217735字节/4属性/HDD源核对通过；维护入口PID不变。首轮进行中中断使用Docker kill接口，Docker将其作为手动停止而不按unless-stopped自动重启，80秒观察超时；不是产品恢复成功。已正常启动同一Worker恢复健康，未替换镜像或回滚发布。固定原测试容器移除、无法继续当前命名空间观察，Root保留schema4 Aborted、SQL到期进入Unknown，未发生发布、屏障false、目标文件和来源均保留。

原临时身份已删除后，使用另一真实管理员正常登录、计划和确认执行永久接管，schema3封存完整原schema4及摘要，原意图以CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED失败对账；没有删除任务/审计/Root/HDD历史。独立核对确切不存在CID、目标全字节及4属性、私有原inode与完整134217735字节HDD来源后，只清理该已证明的测试源。证据 `deploy/artifacts/container-restoration-restart-b8552b3d8f984573a49227239296d819/`，含失败基线、queued与failed-probe-recovered；日志 `/tmp/wd-container-restoration-restarts.log`、`/tmp/wd-restoration-failed-probe-recovery.log`。这验证了真实schema4异管理员永久封存，但不能替代进行中重启门禁。

观察器已改为打开且重新核对固定Worker的pidfd，再SIGKILL实际进程以模拟进程崩溃；避免Docker的手动停止语义及PID复用。失败清理先恢复固定Worker并请求正常取消，等待事实后才退出独占fixture。旧fixture默认仍要求全部核心CID/image/status/StartedAt精确一致，仅本门禁显式允许同CID同镜像Worker的StartedAt变化，不允许重建资源。后续真实三组门禁结果须另记。

## 后续要求

冻结新版本并覆盖，执行排队重启、实际Root partial后的Worker进程崩溃和Agent重启三组；核对不可变计划/来源/属性策略、一次发布或明确失败、目标/完整HDD来源、维护PID与实际HTTP、精确清理和角色。更晚的发布阶段交换/故障、ACL/xattr、重建CID和长期清理仍属于后续，59项active、17项移出不算完成。
