# ACL 写后目标消失：Unknown/明确处置摘要与447070e小屏修复

> 后续实例变化验收：2026-11-03，产品仍447070e，d2ca69d/proofVersion4以新资源补齐写后同CID、不同PID/StartedAt的Unknown/null、不同管理员明确处置、两次真实任务摘要与四次目录延期/释放，独立复核通过；首轮Docker输出断言失败保留。见[后续证据](CONTAINER_ACL_INSTANCE_CHANGE.md)。以下原正文及当时“仍需实例变化”的表述保留为历史，不据此重复执行或修改原结论。

2026-11-03。最终实际运行与成功门禁冻结`447070ea6af1e2c5ea16c76c7d6c9ed476c7c808`、Agent协议1.51，proofVersion3/33依赖与11安装模块逐字节核对。**前两套新资源完整门禁失败并保留；CSS修复覆盖后，第三套全新资源的严格整链与独立只读复核通过。** 不计整体首轮通过，不重放已完成操作或把失败行改成Passed。59主项保持部分实现，master未交付。

## 两次实机失败与可复现修复

80ea8a6与05c2233在612f77c分别使用新的独占资源，确实捕获ACL已写切点、删除本案容器、得到Unknown并完成不同管理员明确确认；但Ack任务详情的“未证明”字段未能完整入视口，因而两轮均失败。第二轮原生居中滚动仍失败，保存的字段盒x82.203125、width238越过320px右边界；文字可见不等于整个字段可见。两轮均按已证明的写后清单清理自有资源，Root/HDD永久保留，未伪造恢复页正常退出或等待目录真实页面通过。

旧测试接口夹具errorCode=null，只改阶段/滚动未复现；补真实`CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED`长错误码后，旧构建在320px暗色复现视口比例0.9991465210914612。447070e为任务详情表格增加固定布局、76px标签列和长内容折行，摘要容器允许收缩且不超过父容器；不改结果事实或降低ratio:1阈值。新增Unknown/Ack字段完整入视口及每个dd不越出抽屉正文边界的浏览器断言，明暗/320×600/1440×600均通过。布局只作用于任务详情，重试表单未使用该class。

前端27项专项、类型/生产构建/lint/格式及8组浏览器（4生产预览摘要＋4本地HTTP SSE）通过。它们使用测试接口，不计真实身份或磁盘验收；真实修复证据由下面第三套资源取得。

## 唯一正常覆盖及运行来源

运行checkout由所有者user从干净612f77c快进至固定447070e；从本机已提交仓库读取同一公共源码，未动私密.env/.images.env、未由root写开发者Git索引。447070e也已正常推送origin/dev。

固定维护helper字节一致，仅启动一次`whaledeck-dev-publish-447070e.service`标准update，Invocation`d6d4eb321eee41038be5981ada2620e2`，09:03:13–09:16:45 CST，约812秒。active/exited、MainPID0、Resultsuccess、ExecMainStatus0；保存回执后停止已结束单元，inactive/dead且仍success。构建耗时期间未重复启动、未自动回滚。

六组件CLI/镜像revision及实际API/Gateway HTTP均447070e。Agent3835700→3947643、维护3747520→3947551，实际exe及映射DLL device/inode与新安装文件一致；8数据库平台CID/镜像/StartedAt不变，11核心healthy。标准migrator实际无待应用迁移，无新增schema/协议/依赖。开发user NOPASSWD文件SHA、root:root/0440及visudo保持，密码和产品Agent最小授权不变；P0-05旧previous-revision缺陷仍另列。

部署原件位于root私有`deploy/artifacts/acl-unknown-width-deploy-447070e/`，含前后基线、唯一单元回执、完整journal及六组件/实际进程证明。

## 已变更物理现场与产品Unknown分开证明

第三套资源为两容器/两自有源目录：均非root1000:1000、无网络、只读根、各自唯一HDD bind，非核心资源。原ACL目标两个文件各2097159字节、原UID/GID2100:3100、固定mtime和普通xattr。正常可见Authentik登录→真实Vue→原版本计划→唯一原执行Job，原Worker只按准确预检正常停启，同CID/镜像保持。

观察器用固定传输argv/root身份、pidfd、准确容器root FD与原操作0600锁FD重新证明：同inode目标access ACL已变为owner6/named user2201权限6/group_obj0/mask4/other0，ctime增加，mode仍0640、NoChange=false；Root Applying、aclConfirmed/actualIdentity=null、version为空、原HDD历史已验证。原计划/原ACL继续指向修改前`original`，另存实际`cutPhysical`；不把计划快照改写成新现场。

仅移除本案准确原容器，保留源目录，记录删除后的完整inventory并与cutPhysical核对。原上下文正常双端退出，产品5.274秒停止原helper，QA未杀helper。原账号随后才准确删除。产品无法继续证明原容器绑定，因而原Root/SQL为Unknown、aclConfirmed=null、publishedVersion为空、屏障保留；**不能从null推断ACL未修改**。主机QA确实看到过写入，原内容、inode、属主、mtime、default ACL和普通xattr保持，access ACL/ctime确实已变；后续只比较写后现场保持。

## 不同管理员处置、目录保护与两次任务摘要

不同临时当前管理员正常Authentik/BFF登录，另一容器目录任务用唯一原ID登记。实际四次自然延期版本`[2,4,6,8]`、四个不同next-attempt，原目标/Root始终不存在；不把初始6秒排队或重复轮询算延期。期间原Root字节、写后源现场及另一源original.bin保持。

当前管理员在真实320×600暗色恢复页先“暂不处置”，原行/屏障/等待目录不改。此时只读原planning/execution Job，SQL/API严格15字段摘要一致：计划仍Ready，执行Running/Unknown、AclConfirmed=null、receiptState=Unknown、bindingCurrent=false、历史验证true、hasPublishedVersion=false。真实任务详情显示未知保护与“未证明”，无通用取消/重试；同上下文新标签页只用于读取和原生滚动，没有注入会话或拦截响应。

随后真实页面唯一一次“确认处置并释放保护”，CSRF/Origin/原ID及版本匹配。原意图Acknowledged、原执行Job Failed，确认审计归当前管理员；原主体/实例/计划/执行ID/第一回执及MAC/aclConfirmed=null/历史摘要/发布版本均不改，Root/HDD raw字节完全保持Unknown。处置只解除保护，不证明ACL成功、恢复或重执行。

确认后再次只读同一执行Job及计划摘要，执行安全摘要为Acknowledged/对应结果码，历史receiptState仍Unknown/null。修复后的真实详情与“未证明”字段均严格完整入视口，Unknown和Ack各保存标题及事实两PNG。两次读取管理写0；原恢复页的唯一确认与这些只读任务页分开计数。

等待目录在原确认之后才启动，原Job/原ID真实Succeeded并在Vue文件列表完整入视口。原ACL固定helper同ID/owner/plan迟到Apply仍Unknown/null/replayed，不重写原件或源；这是固定helper追加探针，不冒充额外签名RPC。最后恢复管理员正常页面双端退出，额外HTTP会话也正常退出；不是删除Cookie/账号冒充退出。

## 原件、清理、独立观察与验证口径

成功批次实际3 Job/7审计：ACL登记/计划/确认/Unknown结果/当前管理员确认各1，目录登记/结果各1。1永久ContainerAclMetadata原ACL历史；本批源与HDD同设备，元数据留存不计内容备份或异盘灾备。

父进程清理前核对完整字节/九字段/两类ACL/xattr与写后快照，准确删除两个自有源/容器，原Root/HDD永久保留。独立新进程复核冻结来源、原Root/attest HDD/第一SQL回执、删容器时完整inventory、四次自然延期、Unknown→Ack两份API/SQL/真实详情证据、明确确认与迟到Apply；临时身份/源/容器不存在，11核心健康、两宿主PID/来源不变，无活动任务/终端/锁/屏障/租约。不声称事后重新读取已删除源字节。

成功9张实际PNG，已目视核对两种“未证明”、确认按钮和目录发布四图；标题/事实分别取景，不用相似截图代替不同业务状态。75相关门禁/范围（16 Unknown＋11浏览器证明＋40原生故障边界＋8范围）及语法通过；没有重新宣称全量后端/前端或整条CI全绿。旧proofVersion2不能签收写后场景，3要求真实Applying切点与cutPhysical。

成功原件root:root/0700目录`deploy/artifacts/container-acl-unknown-affa8192463f48be9789f0b261e51996/`，47份原件SHA目录、JSON/日志/PNG0600；主日志`/tmp/wd-acl-post-unknown-fixed-ui.log`和独立日志`/tmp/wd-acl-post-unknown-independent.log`保留。两次失败目录分别`container-acl-unknown-6de1b4dcc62043429475eefbb5723810`（35原件）及`container-acl-unknown-58d8f36e6d3a4572a1ab3c27532b3764`（37原件），始终不记Passed。第三套是新的随机ID/源/内容/身份，不重复旧修改。预览失败/构建/回归日志见忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/unknown-width-fix/`。

## 尚未签收

本批签收“写后原目标消失、有Unknown回执、当前管理员明确处置及跨容器目录”。仍需实例变化而非消失、其他共享写入/磁盘/路径组合、Unknown无回执或绑定不可信/Superseded/Failed等其他摘要分支、ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D。此前[写后Agent重启](CONTAINER_ACL_POST_AGENT_RESTART.md)与[写入前Unknown](CONTAINER_ACL_UNKNOWN.md)保留各自版本事实，不混算全部权限模块完成。

文档检查：8份变动MD的759条相对链接无缺失，目标第1节与11b563b逐字一致，上批Agent重启QA原正文逐字保留。项目图谱事件`whale-deck.container-acl.447070e.post-unknown`已核对projectId/key/eventId回执并移入synced，阶段仍active。447070e的CI运行41557092143仍在运行，未取得整条成功终态。
