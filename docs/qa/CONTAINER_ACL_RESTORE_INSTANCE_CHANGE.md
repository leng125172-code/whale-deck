# ACL历史恢复写后同CID实例变化：候选与实机验收

## 候选记录（2026-11-04，尚未实机运行）

新增恢复profile8/kind post-acl-instance-change、private proofVersion6，独立于目标移除profile7/proofVersion5。validate_container_acl_restore_unknown.py接受显式post-instance；无参仍严格保持目标移除，其它/混合参数在任何故障动作前拒绝。普通Unknown proof2/3/4、旧恢复profile7及terminal fault验证边界保持，实例模式不能由普通或目标移除证据冒签。

计划先用全新真实Authentik/Vue来源捕获与历史选源恢复，在目标ACL已写、Root仍Applying的精确切点，检查其它Job/终端/锁/屏障/九类文件意图0及仅本轮helper活跃，再对本轮独占容器执行一次固定docker restart --timeout 1。要求CID、镜像、普通UID/无网络/只读根与全部挂载安全配置保持，PID/StartedAt确实改变；重启前后host bind及新实例/proc命名空间完整清单一致，原计划/来源/执行身份不能改写为新实例。

复用不同管理员真实异常面板、暂不处置零确认、原版本一次明确ack、跨容器目录4次自然延期及处置后才写入。Root Unknown/aclConfirmed null/无发布版本、来源/原HDD/迟到apply保持；业务Acknowledged与执行Failed不代表恢复成功。owner/recovery UI、摘要、baseline和来源provenance的实例标记互相核对，缺少实例转换三份回执、旧PID/启动时间、改镜像/挂载或非零其它工作均拒绝。管理员面板来源专属展示仍待，不能用脚本变量补签。

121项相关检查通过（9恢复Unknown、22普通Unknown、28恢复来源、44Linux-root范围、10恢复浏览器证明、8范围），Python/JS语法及git diff检查通过；包含profile7/8、proof5/6、CLI模式混配拒绝和Unknown审计/ack事实保持，原真实实例转换的负例继续执行。它们是隔离/合成输入，不是本场实机通过；本节为冻结前记录，实际结果后续接续。

产品仍b69302b/1.52，Agent1772448/维护1688513，不为验收工具重发生产、不重启宿主或宿主服务。仅本轮明确标记的容器可重启，不操作其它容器；旧失败源树及Root/HDD保持。user NOPASSWD保留、Agent最小授权不扩大，不自动回滚dev。

## 全新资源实机与独立验收通过（2026-11-04）

冻结工具`b33ad6932165ac9917633c7bfeed650fa39f5217`及51份依赖，实际安装helper字节核验；产品b69302b/1.52不重发。主门禁显式post-instance退出0，首次完整实机通过，原件`deploy/artifacts/container-acl-unknown-6db5d4251ec1416984b635ac7cb7f811/`，profile8/private proofVersion6。

两套新独占资源，正常可见Authentik→Vue本轮来源捕获→公共历史List/Read选源→新的恢复预检与明确确认→永久PG/Worker/签名Agent→实际ACL/HDD。只在目标基本access ACL已写、Root仍Applying且当前原件HDD已验证的精确切点，对准确本轮CID执行一次docker restart --timeout 1。其它Job/终端/锁/屏障/九类写入为0、仅本轮helper活跃；未操作其它容器、Agent、维护服务或宿主。

CID、镜像、全部挂载与容器安全配置保持；PID1886629→1890996，StartedAt从2026-11-03T16:33:02.212317192Z变为16:34:13.87697475Z，实际Kernel实例改变。重启前/后host bind和新/proc实例命名空间逐文件清单均等于已写切点；原command.instanceId、canonical历史来源、原登录/请求/计划/执行hash仍绑定旧实例，未借同CID改绑。原用户正常双端退出，产品4.661秒结束helper，验收器未杀helper。

本场是**实例变化加原上下文正常退出**组合，不单独归因为仅重启，也未签“重启后持续保留原登录”的隔离组合。Root Unknown、aclConfirmed=null、无发布版本，不能根据现场基本ACL断言历史恢复成功；业务Unknown/执行Running与保护保持。

不同新管理员真实320×600暗色面板先暂不处置、确认0，再用原version明确ack一次。Unknown/ack两阶段Job摘要与API/SQL一致，确认仍显示“未证明”、回执Unknown/绑定无效/无发布版本，未提供通用写按钮。原恢复业务Acknowledged、执行Failed；5条恢复专属审计/不同ack演员/historyRestore与restoreSourceHash验证通过。原Root/HDD/来源字节保持，迟到原Apply仍Unknown/replayed，未重写或改绑新实例。

另一容器目录按原ID版本2/4/6/8自然延期，4轮均无Root/目标。00:35:14.582729+08:00明确处置后，原等待任务00:35:18.063014+08:00才实际Root写入并Succeeded，Vue列表真实可见。两文件各2097159字节、原inode/属主/mtime/普通属性与非目标ACL完整核对，目标ACL/ctime按已写切点单列；所选旧来源与本次当前ACL前像分别验证，不能混用历史digest。

9张本批真实PNG含原进行中、目标/明确确认、Unknown/ack任务标题及事实、等待目录可见；手动复看移动端确认区与ack事实，按钮完整入视口、长ID换行、滚动可读且无横向溢出。原Owner真实选源/恢复wrapper与不同管理员Owner接口404通过；管理员仍为通用异常面板，不把脚本绑定值冒充来源专属展示。两人分别在原上下文正常双端退出，用于登记目录的独立HTTP会话亦正常退出。

独立verify_container_acl_restore_unknown.py退出0，independentAclRestorationUnknown=true、sameCidInstanceChanged=true；重读SQL/Root/attested HDD/来源、实例转换请求/响应/ack后三份证明、5恢复审计、Job、4自然延期与目录实际发布时间，准确源/容器/临时身份均不存在。它不在清理后重读已删除文件；完整物理由主验逐轮读取。没有为独立通过重做重启、恢复或ack。

最终六组件只读probe仍b69302b，Agent1772448/维护1688513不变，11核心healthy；8数据库平台及API/Gateway保持原ID/镜像/StartedAt，Worker只为精确切点正常停启。7配置/密钥/sudo规则SHA/uid/gid/mode与可信账本保持，活动项0。133份旧HDD元数据字节及九字段逐份保持，库存135只新增本场来源/当前前像2份；三批旧失败源树仍在，不算长期清理完成。预检/最终边界`deploy/artifacts/acl-restore-instance-preflight-9660e573f8694e818b41e8ec054f153e/`，原件root私有、SHA留证、凭据不进仓库或日志。

下一步管理员Recovery专属来源摘要/UI，并保留owner恢复接口与普通Job摘要权限边界；再来源丢失/替换、其它故障与磁盘共享、递归ACL/恢复、删除长期清理及其余59项。源和HDD同device，不算异盘灾备，59主项及六阶段完整签收数不提升，17项移出不算完成，master未交付。

收尾8项范围测试、883条变动MD相对链接通过，目标第1节逐字保持、2份QA原文仅追加。准确工具[CI run41675262689](https://github.com/leng125172-code/whale-deck/actions/runs/41675262689)仍in_progress，未签成功终态。图谱确认原whale-deck/container-acl的`whale-deck.container-acl.b33ad69.restore-instance-unknown`事件，阶段active，pending移入synced；前批原事件也已成功补同步，不计59主项全部完成。
