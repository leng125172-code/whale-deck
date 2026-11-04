# 递归 ACL 历史恢复真实页面门禁

2026-11-05，产品运行版本保持 `eeb24dd1b9d55062dfb53b25527973817c9722f9`；门禁工具先冻结cba1292，JSONB与图片计数修正7837675。承接[标准覆盖及迁移](CONTAINER_RECURSIVE_ACL_RESTORE_RELEASE.md)。本报告区分真实分项、失败、工具修正及尚未执行范围，59项整体和完整权限功能不升完成。

## 门禁及准备

新增真实浏览器编排和独立校验，使用实际Authentik可见登录、Vue历史来源选择、异构目标预览、预检/确认/取消、原引用重载与相同key/原计划确认幂等；不拦截响应、不注入Cookie或操作本地登录表。来源与恢复使用全新自有受限容器和目录、不同意图/执行ID及独立域；容器无网络、非root、只读根、受限资源，普通文件2MiB+7字节。初始one.bin预置真实ID0/mask2 ACL作为历史前像，不把ACL条目ID当inode所有者。

同一真实管理员先通过正常递归ACL页面生成7份成功来源（含no-op/ID0/default空）与1个写前取消，然后依次restore-access、restore-noop、restore-default、restore-clear、restore-cancel五场景。每轮浏览器等待另一父级读取真实SQL/双Job/outbox/审计、实际全字节/身份/ACL/xattr、原Root与attested HDD聚合/子项后才进入下一轮；取消还验证确定性执行ID对应的Root/HDD缺席。父级之后另启独立只读进程重开真实源文件，再精确清理；清理后最终进程只重读保留记录，不宣称重读已删源字节。

9个新门禁专项、65项关联Root工具（含新专项）、8范围及严格Python/Node语法首先通过；依赖闭包93份源码逐Git blob/工作区核对。JSONB修正后关联66项通过，CI已接新工具。离线数据投影及临时ACL测试不当作真实页面或生产SQL通过。

## 首批失败与已保留真实分项

冻结cba1292，unit `whaledeck-qa-recursive-restore-ui-cba1292`，Invocation `d8eca2dc57e445b18c5628893247870a`，实际ExecMainStatus=1，整批失败。8轮新来源已由父级逐轮真实核验，首轮access恢复也在真实页面Succeeded，但父级读取双Job元数据时，把PostgreSQL JSONB投影对象误当成JSON字符串而拒绝。SQL实际Succeeded、类型/计数/摘要一致，属验证工具错误，没有产品发布或回滚。

原容器按已有自有资源规则移除，未证明完整清理的源目录保留。7837675修正工具支持JSONB对象和严格文本两种元数据、补充回归；原捕获/恢复不重发，只读重新获取原SQL/Root/HDD和保留的真实文件，4子项、实际异构ACL/ID0/非目标属性/全字节均核验后才精确删除源。补核时首次相对证据路径被trusted路径规则拒绝，改绝对路径后通过，未放宽路径保护。最终1504普通保护记录、来源Root1122/恢复Root42-HDD52，原1434全部保留；8新来源/1恢复为分项，后4恢复及原上下文正常双端退出均未验，不能补签原总门禁。

实机结束前的只读代码复核另发现最终图片就绪/结果子计数漏算新增5恢复场景，等待原父级结束后再修正只读验证器，未改运行中冻结代码。原cba93源码闭包、结果、失败回执及补核均保留。

首批私有证据 `deploy/artifacts/container-recursive-acl-browser-16cb6fe9f1784908a04d28f63ea9bc2a/`；`partial-recovery.json` 明确originalParentGatePassed=false、normalDualLogoutNotAccepted=true、remainingFourRestoresNotRun=true。没有把被终止浏览器或临时账号删除当作正常BFF/Authentik退出。

## 修正后新批次

冻结7837675、93依赖哈希一致，在完全不同的新容器/源目录/账号/意图/执行ID上启动 `whaledeck-qa-recursive-restore-ui-7837675`，Invocation `3450d1dcd24f4129945c929c8cc33583`。证据 `deploy/artifacts/container-recursive-acl-browser-474887bd929e47e1b644954885bfa172/`。首批所有ID保留，不再次发送首批请求。

新批次主门禁成功：SubState=exited、Result=success、ExecMainCode=1（CLD_EXITED）、ExecMainStatus=0。8个来源场景7成功/1写前取消，5个恢复场景4成功/1写前取消；原上下文管理员及普通身份均完成正常BFF/Authentik退出。独立清理前进程实际重开4个对象全字节、身份/ACL/xattr、SQL及原Root/HDD，返回independentLiveRestoreProof=true；随后准确移除自有源目录/容器及临时身份。清理后独立验证器退出0，返回independentRecursiveAclRestoreBrowser=true、temporaryIdentitiesAbsent=true、coreUnchanged=11，不声称再次读取已经删除的源字节。

| 真实恢复场景 | 结果 | 本次实际修改 | HDD逐项留存 |
|---|---|---:|---:|
| restore-access | Succeeded | 4 | 4 |
| restore-noop | Succeeded，原历史再次恢复 | 0 | 4 |
| restore-default | Succeeded，文件保留access与其他属性 | 2 | 4 |
| restore-clear | Succeeded，目录default恢复为空 | 2 | 4 |
| restore-cancel | Ready取消，先撤销取消框再明确取消 | 0，未执行 | 0 |

来源原changeCount与恢复changeCount分别绑定；ID0、mask、文件default不适用和目录显式空逐对象展示并核对。5次实际页面登记、4次实际确认、1次实际取消；相同key重复登记返回原意图，原计划重复确认返回原执行，重载保留四字段opaque引用。没有内容上传。恢复普通身份4GET+4POST均403、退出后4GET均401，普通页面私有恢复请求0；CSRF缺失400、错误Origin403、来源摘要字段缺失400。额外HTTP幂等/拒绝请求独立记录，不算新的页面操作或执行。

本批正常来源新增7聚合/28子项HDD，恢复新增4聚合/16子项HDD；共60张实际PNG（恢复30张），34张320×600、26张1440×600，恢复场景交替暗/亮主题。人工复核320暗色Ready/default结果及1440亮色清空default结果，长摘要换行、逐项入口与底部关闭/查询/确认/取消均可见；这不是全部页面/设备验收。

最终生产普通递归ACL54意图/49执行；恢复6意图/5执行（含首批独立1成功，第二批4成功+1取消），恢复活动0。普通保护1574、来源Root1192、恢复Root82/HDD76、目录Root94；第二批前1504保护和所有Root/HDD旧文件九字段/全字节保持，首批前1434旧记录通过连续证明保持。11核心/两宿主身份仍为eeb24dd且不变；本批没有产品发布、Worker/Agent重启或自动回滚。

本轮复跑9门禁+10业务证明测试全部通过、零跳过，并检查Python/Node语法；先前66项关联工具结果保留，不称本轮全部重跑。CI去掉新增门禁的重复非root执行，保留一次sudo以实际执行临时ACL测试；7837675远端推送前两次TLS失败，随后普通推送成功并ls-remote核对，不把重试算重复部署。最终MD范围/链接检查见下方收尾记录。

## 验收边界与下一步

本批签收常规真实身份→Vue来源/逐项预览→API/Worker→签名Agent→实际文件/SQL/Root/HDD及原上下文退出链路；不签完整递归恢复故障矩阵。写入中Partial/Unknown、写前/写后取消和撤权、Worker/Agent进程中断、来源异常/实例变化/跨写入及跨管理员处置需在恢复类型上独立验收，不能借正常递归ACL旧矩阵替代。恢复前像仅4条单页，realSourceSnapshotPagination=false；历史列表与正常历史前像旧跨页记录不冒充恢复预览真实跨页。

可恢复删除/长期清理、维护入口完整连续性及其余59项仍待完成；旧维护失败和源/HDD同device限制保留，不升整项或阶段完成率，17项移出不算完成。


## 收尾记录

本轮最终8项范围测试通过；修改文档727个本地链接存在、缺失0，原发布QA正文逐字保留为新增说明前缀；CI YAML解析及git diff --check通过，Git索引仍1000:1000/664。19项Root专项已通过，未将正常数据夹具或旧大测试总数冒充本轮实际执行。

项目图谱事件 `whale-deck.container-acl.7837675.recursive-restore-real-ui` 首次传输失败，按同eventId重试取得project/key/event匹配成功回执、duplicate=false；pending移入synced，container-acl保持active。

首批失败证据210文件与成功批287文件分别写入私有SHA256清单并逐项复核；清单SHA256为`71db2b78373f13a0d9fe4e98bd1cf1219a83608845b90603ca29ad2e728344d9`、`433bf9672b23965b1acab83bb34445c71ca53031243f41c0bc8c9a255e943d67`。QA unit成功回执归档后才stop（仅已退出QA unit），原产品宿主和容器未重启；临时人工截图查看副本已清理。
