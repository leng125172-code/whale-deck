# 递归 ACL 历史恢复管理员 API 与 Vue 候选

2026-11-05，API/Vue功能提交 `0034671`，范围元数据修正 `7906e45`。承接[真实持久化/共享保护](CONTAINER_RECURSIVE_ACL_RESTORE_PERSISTENCE.md)、[内部编排](CONTAINER_RECURSIVE_ACL_RESTORE_BUSINESS.md)与[安装态RPC分项](CONTAINER_RECURSIVE_ACL_RESTORE_AGENT.md)。**本批是候选接口和页面验证，未部署生产迁移/API/Worker/Vue，未新增真实Authentik→Vue→Agent→文件/HDD恢复整链；59项主任务完整完成率不升。**

## 接口与页面

新增管理员 `containers/{containerId}/files/recursive-acl-restores` 预检登记、按原ID/原key查询、确认和取消；`files/container-recursive-acl-restores/recovery` 提供有界Partial/Unknown清单、原任务读取与明确处置。BFF Cookie/CSRF/Origin沿用实际管线，服务持续检查当前权威管理员、原登录绑定与原计划；所有接口no-store，写入体上限64KiB。恢复请求仅允许instanceId/path/captureId/expectedHistoryDigest/expectedVersion五字段；确认和处置要求明确规范版本/布尔值/原幂等键，拒绝缺失、重复、额外字段及数值强转，错误不回显私有JSON。GET参数拒绝未知/重复/非规范take，清单上限50；不把控制器变成同步递归文件写入入口。

Vue从已核实的递归历史前像选择来源，用文件列表中原对象版本生成异步预检；独立恢复任务、确认、取消、状态和异常处置接真实业务服务。页面展示来源捕获、历史/清单摘要、原实例/时间、预检根与执行身份、文件/目录及本次预计恢复计数、原执行Job、五项实际计数、HDD历史事实和最近回执观测。来源原计划的changeCount与本次恢复changeCount分列，允许来源原计数0，不伪装为已执行数量。

恢复目标是按路径和对象类型匹配的异构ACL，不是普通递归ACL的统一文件/目录目标。专用只读来源面板每页最多25对象，展示原修改类型的实际恢复条目、ID0、mask有效权限及default空清除；default恢复明确标记文件不受影响。目录最后、完整当前前像留存、来源变化围栏及Partial/Unknown不续做由已有Agent/Worker编排执行，页面不声称恢复旧inode或所有者，也不把另一类历史ACL作为目标。

独立四字段opaque引用只在sessionStorage存subject/container/key/id，不存来源capture/digest/ACL/确认token。丢登记/丢确认/重载继续查询原引用，禁止自动换key或重发确认；计划期限、实例变化、乱序/变更来源/不一致计数会阻止确认。初次身份暂不可用时保留opaque引用，权威401/403、换用户/降权或明确退出清理；晚到页面请求与卸载通过abort/generation隔离。所有旧文件修改入口纳入新页面锁，通用任务重试拒绝，Partial/Unknown阶段不提供通用取消；当前管理员通过专用处置核验原执行，不冒称恢复成功。

页面遵循Element Plus变量、现有明暗主题和紧凑双列指标卡。对话框固定头/尾、正文滚动和小屏折行；320×600下确认/取消及长路径处置按钮可见。只读历史接口签名收窄到实际需要的containerId/instanceId，不生成假的容器展示字段来凑类型。

## 自动化验证及真实边界

5048后端单元、660默认集成通过（新增47接口相关用例，另7原生默认跳过），Release零警告/错误；1259前端单元、类型、生产构建、lint及格式检查通过。新增HTTP用例包含实际回环MVC/Cookie/CSRF/Origin、普通/匿名全部路由拒绝、当前管理员撤权、原登录隔离、同一原key与确认执行幂等、缺来源/严格JSON、查询边界及无同步Apply；身份目录、仓储和Agent为显式夹具，不算Authentik/真实SQL或磁盘证据。

开发页面新增15用例通过，随后生产构建预览新增29用例通过，覆盖三宽度明暗、来源与异构分页/ID0/default空、丢登记/确认重载、Partial/Unknown、明确取消、丢ACK原查询、401/403/404/503、实例变化/过期、错误来源/计数、长路径小屏处置及晚到读取关闭。生产预览仅固定回环5175，浏览器请求由测试接口提供，不得当作工作站真实页面整链。

最终生产预览96项全部通过（29新增、67关联），skipped/unexpected/flaky均0，六张320/1045/1440明暗PNG已归档；47接口相关用例在冻结构建后另执行，仍全部通过。8范围检查修复元数据后通过；不因默认原生跳过重跑已完成的实机捕获/恢复或模拟HDD验收。

## 失败、修正与证据

首次类型检查发现历史读取签名额外要求name/image/configuredUser，已改为实际身份字段；一次新增测试fixture触发CA1859，使用具体FilePlanProtector类型修正。首次浏览器用例因预期英文user 0而实际页面为“指定用户 0”失败，修正测试标签后通过；不把它记成产品权限错误。新增单测缺少toThrow消息、一个Vue文件格式问题已按现有规则修正，未降低lint门槛。两次本地编辑脚本工作目录前缀错误在落盘前退出，随后用正确路径保存，不当作实现成功。

本轮8范围检查发现上一批nextStep漏保留“17项移出不算完成”，已在7906e45恢复并重新通过；59项目标正文与17项移出决策未改变，未削弱检查。早期默认集成/单元回执与本批最终结果分开，不把构建前旧二进制作为新增接口证据。

私有证据 `deploy/artifacts/recursive-acl-restore-api-ui-20261105/` 保存TRX、Vitest JSON、构建/检查日志、浏览器JSON、源码来源与运行观察；首轮失败浏览器的临时test-results已被后续Playwright重建，不声称该临时截图仍在，失败文本和修复事实保留在本报告。明暗320截图已实际查看，属于测试接口的候选UI证据。26冻结文件（含1范围元数据）与7906e45核对，25代码文件在MD更新后仍一致；Agent/MaintenanceHost/Worker三个候选CLI均返回准确7906e45来源，未启动候选服务。725关联Markdown链接存在，原目标/实施状态/持久化报告旧正文校验保持。

只读与上一持久化批次快照比较（不是本轮起始快照）：1434受保护配置/元数据、来源Root1052、恢复Root32/HDD46、目录Root94的九字段与字节hash保持；11核心身份/健康与两宿主e066ec1（Agent1080518/Maintenance1080514）保持，活动0。未执行部署、真实恢复、身份修改、宿主重启或自动回滚。前批维护67样本4次非200和源/HDD同device2049的限制保留。

后续需准确CI成功终态、标准覆盖部署与迁移，再使用全新自有资源完成真实Authentik登录→来源前像选择→Vue预检确认→API/Worker→安装态签名Agent→实际异构ACL/HDD记录的独立整链核验；随后完整恢复故障矩阵、可恢复删除/长期清理、维护连续性及其余59项目标继续。

功能0034671及范围修正7906e45已推送origin/dev；首次推送TLS中断，单次HTTP/1.1重试成功，无强推或全局代理/TLS改动。准确CI41893214030最后观察仍in_progress，未作为成功终态或发布许可。

项目图谱已获匹配事件 `whale-deck.container-acl.7906e45.recursive-restore-api-ui` 的成功回执，pending移入synced，阶段仍active；这不是整阶段完成或部署成功的证明。


后续接续：eeb24dd准确CI成功后已完成标准覆盖与生产迁移技术核验，见[发布批次及未验边界](CONTAINER_RECURSIVE_ACL_RESTORE_RELEASE.md)。本文候选时点的测试夹具和未上线事实保持，不以发布技术核验补签真人恢复整链。
