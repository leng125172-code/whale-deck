# ACL历史恢复真实页面、部署与独立验收

2026-11-03。产品冻结`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`；真实验收工具冻结`2880f7bfaba8bfe8d0c7dd19b9d1ac391a814c87`，29份工具源码逐字及SHA核验。以下是本批新独占资源的实际操作，不是把早先隔离PG、模拟页面或安装态RPC拼接为恢复整链。

## 标准发布与CI

- 准确候选cad6182的[CI run41619582863](https://github.com/leng125172-code/whale-deck/actions/runs/41619582863)五路均completed/success：backend、installer、native-postgres、native-valkey、frontend。前端四组实际结果为8、81、242、409 passed；先前79/81和80/81的失败记录保留，不改写为首轮全绿。
- 唯一标准维护update：`whaledeck-dev-publish-cad6182.service`，Invocation`0efd3714f17b44209562fc73c12e8ffd`。终态active/exited、MainPID=0、Result=success、ExecMainStatus=0；保存该Invocation日志与终态后才停止已结束unit。首次启动客户端等回执30秒超时，但unit继续运行；未因此重复启动发布，也不把客户端超时记为发布失败。
- 标准流程重新构建四镜像、安装宿主、应用生产`20261103085710_ContainerAclRestoreSources`迁移并替换API/Worker/Gateway。实际镜像ID、CLI/build-info及标签同源cad6182；两宿主实际exe来源相同，Agent PID1214295、MaintenanceHost PID1214273，协议仍1.52。不是仅凭源码HEAD或可变latest标签推断成功。
- 生产`restore_source_json`列存在、CHECK=18且迁移入库；账本current=cad6182、previous=08b2bb8、pending=null。11核心healthy，8数据库平台容器ID/镜像/StartedAt不变；7份受保护配置/密钥/sudo规则仅比SHA、uid/gid/mode且全部保持，未保存其内容。user NOPASSWD保留，产品Agent授权不扩大。
- 安装后`deploy/host/validate.sh`退出0：self-contained .NET10.0.13、权限、UDS、固定helper、HDD策略及动态发现维护入口通过。无主机重启、自动回滚或内部端口暴露。

准确候选远端CI成功后停止了重复的本机最终409案例诊断，原件为108 passed、1 interrupted、300 did not run（exit130）；截图来自主动中断，不记为产品回归失败，也不声称本机409全跑完。远端409全通过与本机中断分别保存。

## 新独占资源真实整链

主验`validate_container_acl_restore_browser.py cad6182…`退出0；本批证据目录为私有忽略路径`deploy/artifacts/container-acl-browser-59582492cd604e6fab95ceb964b943bc/`。正常可见Authentik登录→实际Vue→BFF/API→永久PostgreSQL/Worker→签名Agent→真实文件ACL/HDD，未拦截路由、注入Cookie、改SQL状态或复用旧验收操作。临时账号凭据只在进程内存/子进程环境，不入仓库、命令参数或证据日志。

先执行9个新基础ACL场景（8完成/1执行前取消），生成8份本批新捕获；再从它们的历史页面实际选择来源，完成6个新恢复意图、5个新执行、5份新HDD记录：

| 场景 | 实际结果 |
| --- | --- |
| restore-root | 恢复包含指定UID=0、权限=0、mask=6的access ACL，Completed |
| restore-basic | 恢复原基本access ACL，Completed |
| restore-noop | 同一基本ACL无需变更，仍保存当前原件并Completed |
| restore-default | 恢复目录原具名default ACL，Completed，现有子项保持 |
| restore-clear | 恢复空default ACL，Completed，现有子项保持 |
| restore-cancel | 预检后正常取消，Canceled，无执行ID/Root/HDD新增 |

每次只恢复来源的changedAclType（access或default），另一类ACL、文件字节/原inode/uid/gid/mtime/普通xattr与现有子项均按每次前后清单校验。两文件各2,097,159字节，不以首尾抽样代替完整校验。所选旧Root/HDD逐次保持，新执行UUID不同于旧捕获；新HDD保存的是**恢复前当前ACL**，与目标历史ACL分别核验。原8份与新5份共13份Root/HDD保持，SQL完整canonical来源、来源hash、Job元数据与23条专属恢复审计逐项对应；审计不存ACL正文。

真实Vue完成6次原登记HTTP重试、5次原确认HTTP重试、刷新仅GET原引用；正常页面只发6登记/5确认/1取消。来源、当前与目标ACL、显式确认及完整终态均有实际截图。320×600暗色/1440×600亮色交替，确认/取消完整入视口且无横向溢出；人工复看320暗色restore-root预检及restore-clear完成页，零值/空default、当前原件留存及动作区可读。共22张本批真实PNG（基础9、恢复12、普通用户1），不是测试接口截图。

普通用户正常登录后恢复相关3 GET+3 POST全部403，管理页forbidden且恢复UI请求0；原普通会话正常双端退出后3 GET均401。管理员原上下文亦正常页面双端退出，原BFF与Authentik状态分列检查。基础分支另保留11普通拒绝/7匿名拒绝及CSRF/Origin/缺字段拒绝，不混算为新增恢复数量。

本批检查17个基础新Job与11个恢复新Job的SQL/API摘要；**没有重跑18个旧任务详情抽屉**，actualTaskDrawers=0，不用早先任务中心截图补签本批。原ACL来源显示、恢复预检和执行详情是本批真实页面范围。

## 独立复核与清理边界

`verify_container_acl_restore_browser.py container-acl-browser-59582492cd604e6fab95ceb964b943bc`退出0，`actualAclRestoreIndependent=passed`。独立读取永久SQL/Root及受信HDD，核对每场景来源/结果/审计/原始与当前ACL前像分列、13份历史及两上下文正常退出；确认准确临时身份、源目录与容器已不存在，11核心不变。文件物理字节由主验在每次操作前后完整读取，**独立工具没有在清理后重读已删除源文件**（sourceBytesRereadAfterCleanup=false）。

主验完整首轮成功，准确清理本轮已证明资源；没有为了让独立检查通过重做真实写入。发布前失败、CI修复和中断诊断的事实仍见[预构建基线](CONTAINER_ACL_RESTORE_PREBUILT.md)、[发布准备](CONTAINER_ACL_RESTORE_RELEASE_PREPARATION.md)、[API/Vue候选](CONTAINER_ACL_RESTORE_WEB.md)及[永久化候选](CONTAINER_ACL_RESTORE_PERSISTENCE.md)。旧QA中的“未部署/未验”指各自记录时点，由本记录接续。

发布原件位于`deploy/artifacts/acl-restore-release-cad6182/`，真实整链原件位于上述独占目录，均root私有且不入Git；归档SHA清单单列原始预构建与本批最终证据，不覆盖旧清单。

本批只签正常**单对象ACL历史恢复**，包括写入前取消。源与HDD同device，不算异盘灾备；未补签恢复进行中撤权/取消/重启/Unknown、来源丢失或替换、磁盘故障、完整共享写入组合、递归ACL/递归恢复、可恢复删除/长期清理或安装态物理冷启动。59主项及六阶段完整签收数仍0，40项部分实现和25%–35%工作量粗估不因本分项提升；17项移出不算完成，master尚未交付。

收尾8项范围检查通过，852条变动MD相对链接无缺失，目标第1节逐字保持、4份旧QA原始正文逐字保留后追加接续。项目图谱已确认同一`whale-deck/container-acl`的`whale-deck.container-acl.cad6182.restore-real-ui`事件，阶段仍active；首次读取连接失败后重试取得准确登记/阶段回执，pending按原eventId移入synced，无重复创建项目或宣称全目标完成。
