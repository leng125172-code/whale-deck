# 递归所有者：覆盖部署、真实六场景与退出观察补证

2026-11-02。实际发布`b1fafedddfa1767d2d573bd94d2748f3cb5e7b44`，协议1.50；验收源`c6632bf`，退出观察及原证据恢复源`5c261fd`。本批签收常规真实Authentik→Vue→API/SQL/Worker→Agent→目录树/HDD六场景的事实，并单列失败与补证。**原整套脚本在最后双端退出检查失败，未改记首次完整通过；没有重复任何所有者修改。** 进行中取消/退出/撤权/重启/回执丢失、Partial/Unknown及其余故障组合仍需真实业务验收；ACL、删除/长期清理与其余59项继续，17项移出不算完成，master未交付。

## 一次正常覆盖部署

运行checkout `/data/GitRepos/whale-deck`由所有者从干净dev/0dede08快进至固定b1fafed。全局任务/终端/主机锁与各旧写入屏障均0，递归chmod活动/租约/屏障0，递归所有者新表尚不存在；确认固定维护helper字节与源码相同后，只启动一次`whaledeck-dev-publish-b1fafed.service`正常update。

Invocation `32e9789056bf4728b58f189d160abefe`成功结束，ActiveState=inactive、MainPID=0、Result=success、ExecMainStatus=0，原journal有Deactivated successfully，墙钟334.059秒。不自动回滚dev，不重复启动发布；既有P0-05上一版本来源问题仍单独跟踪。

六组件实际build-info、API/Worker/Gateway镜像revision与HTTP均匹配b1fafed；实际宿主MainPID的exe device/inode、映射DLL device/inode匹配新安装文件，两宿主按正常安装重启，非仅磁盘文件更新。标准安装/依赖/runtime/UDS/helper/HDD/动态入口validate通过。生产迁移`20261102131222_ContainerRecursiveOwnershipIntents`已前向应用，验收前新表0行/0屏障；未用EnsureCreated或SQL/缓存注入。

11核心healthy，8数据库平台容器的ID/镜像/状态/StartedAt保持原样；API/Worker无Docker Socket、privileged或宿主端口。user的sudo -n与固定NOPASSWD文件visudo检查通过，保留授权，不改密码；产品Agent最小授权未扩大。发布源CI41456347227已确认后端/native-postgres/native-valkey/installer成功，前端仍在运行时不冒称全绿。发布回执、前后基线、完整journal及来源证明位于root私有`deploy/artifacts/container-recursive-ownership-deploy-b1fafed/`。

## 六个真实页面场景与逐轮实盘核验

仅一个准确随机、非root1000:1000、无网络、只读根、唯一自有HDD bind目录的容器。树为2文件+2目录，每文件2097159字节，混合原UID/GID和普通模式、固定mtime、user.whaledeck-recursive属性。原根初始2100:3100，容器执行身份1000:1000；数字目标由页面显式输入，与Authentik管理员Subject无关。

正常可见Authentik登录，实际Vue选择目录/预检/明确确认或取消。浏览器每轮都等待父进程核对完整源字节、九字段、SQL/Job/审计、Root/HDD原件后才继续。没有Cookie注入、响应替换或产品故障钩子。

| 场景 | 显式目标UID:GID | 实际结果 |
| --- | --- | --- |
| complete | 2200:3200 | 4项完成核对、3项修改；本来已匹配的一项完整九字段不变 |
| noop | 2200:3200 | 4项完成、0修改，全树九字段不变 |
| alternate | 2300:3300 | 4项完成、4项修改 |
| zero | 0:0 | 显式root，4项完成、4项修改 |
| readable（既有内部场景名） | 2200:3200 | 4项完成、4项修改；仅返回先前数字目标，不声称恢复容器用户可读性或最初混合属主 |
| cancel | 2400:3400 | Ready后先继续等待再明确取消；无执行Job/Root开始/回执/HDD，整树不变 |

原6次登记重试、5次确认重试只识别原ID/原执行；刷新接续原引用，不重新预检或确认，文件内容PUT为0。原浏览器已在退出前执行这些网络计数断言并发出全部6个结果/父级核验信号。永久SQL为6意图、5执行Job、5 Succeeded/1 Canceled、同一原Subject的23条递归审计；11唯一Job的原主体与元数据哈希对账。审计不存路径/计划令牌/密码。

5份聚合Root/HDD及20份逐项Root/HDD均独立重算原计划、清单、子计划、历史、公开版本和outcomeDigest；目标UID/GID与原inode、完整内容、普通模式、mtime、普通属性核实，仅显式属主及必要ctime变化。HDD保存属主元数据，非文件内容备份；源与HDD同实际设备，不计异盘灾备或NVMe业务覆盖。

6张真实PNG及SHA/尺寸保留：1440/320明暗，含320×600预检取消；已目视核对显式0:0及取消截图，原根/目标/执行身份清楚，正文滚动、确认/取消可见，无横向溢出。不复用生产预览截图。

## 失败、观察器修正与不重做的恢复

原父进程在6个physicalAndHistoryVerified信号全部落盘后，收到浏览器退出失败：phase=visible-dual-logout、AssertionError。原`browser.stderr`和父stdout的Failed保持原样；fixture只移除准确CID，未验证清理的测试树保留，Root/HDD永不删除，未生成伪造的browser.json或verified.json。

旧退出观察器看到SPA短暂`/login`就立即查会话；顶层OIDC导航当时仍在进行，失败截图是Authentik加载页。修正验收器而非产品：先等待可见用户菜单就绪，再观察真实顶层退出POST302及供应商end-session导航，限时轮询Whale Deck401与Authentik401/403，最后核对返回/login。只读取状态，不导出URL令牌/Cookie、不替换响应。

独立auth-only新临时管理员的首轮因页面尚未就绪，用户菜单未进视口而失败；加就绪等待后再次纯登录/退出通过：POST302、供应商导航观测、Whale Deck401、Authentik403、最终/login，fileMutations=0。两轮临时身份准确清理，首轮日志保留。**补证使用另一个身份，不宣称原六场景上下文已通过双端退出**；未重复六场景或任何fchown。

`recover_container_recursive_ownership_browser.py`要求原6个结果与核验信号齐全、冻结源摘要/旧失败位置一致、永久SQL/Root/attest HDD重新核对，且新的独立auth-only证明有效；回读原件存单独目录并与原字节比对，不覆写原证据。它实际重读保留树最终完整字节/属性，对照最后物理证明，核实原父目录device/inode、准确随机路径及original.bin确定内容/UID/GID/模式；确认原CID已不存在后才清理准确自有树和文件。普通8路403/匿名4路401在此阶段针对原永久任务/路由补查，明确原CID当时已移除，未重建容器或新增文件写入。

恢复回执为`recovered.json`、`browser-recovered.json`，normalDualLogout=false、separateAuthOnlyLogout=true、originalGateFailedAtLogout=true、noRepeatedFileMutation=true。这是分项恢复签收，不是首轮整套通过。

## 独立只读核验与未验边界

另一root只读进程重读原SQL/Root/实际attest HDD、6组信号、冻结源与独立退出证明，独立重算并核实准确源/CID与临时身份不存在、11核心ID/镜像/StartedAt不变。`independent-final.json`为Passed，cases6、aggregateHddRecords5、childHddRecords20，originalGateRecoveredAfterLogoutFailure=true、normalDualLogoutFromOriginalContext=false、separateAuthOnlyLogout=true；不声称清理后重读已删除源字节。

最终活动任务/终端/锁、所有八类容器意图/屏障/租约及递归所有者transient unit均0。核心11healthy，两宿主active，原件/HDD保留。17项本批离线范围/边界检查、Python -W error/Node语法与文档检查通过；起初新增工具的复数模块import适配错误已修，未因此执行真实操作。此前4036 Unit/363 Integration（7默认原生跳过）、909前端Unit和44+112生产预览是前批候选证据，本轮不冒称全部重跑。

真实证据目录`deploy/artifacts/container-recursive-ownership-browser-3a8c84009a034ea38ba8a2f13cc44cb7/`为root私有0700，正文/JSON/日志0600；密码与API凭据只在内存/子进程环境，不进Git或日志，不截图输入凭据页面。原/补证/恢复/独立日志分别为`/tmp/wd-recursive-owner-{browser-first,browser-recovery,browser-independent,deploy-verify}.log`及目录内auth-only两轮原件。后续需补本功能的真实进行中故障与同上下文完整退出组合，再推进ACL、可恢复删除和长期清理等剩余目标。

文档检查：214份Markdown/1298处相对链接无缺失，git diff检查通过；前批页面候选QA原始正文逐字保留，只前置后续说明。
