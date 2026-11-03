# ACL恢复：异步预检前有效来源替换

2026-11-04，仅新增签收真实登记已接受Queued、异步预检前来源被同captureId的另一有效记录替换。显式profile8/source-plan-replaced，产品仍28062da/协议1.52，无发布、宿主/Agent/数据库重启、SQL/缓存/系统时钟修改；仅停/启原Worker。此前缺失、首次登记、Ready确认及首次Apply替换各自原件保留，不重放。

## 实机及独立版本

实际门禁冻结`bd72c457e34da57ea60bbb7acdbeb765111fc5ae`，干净树、运行前102文件Python/MJS闭包及13安装helper逐字核对，退出0。成功后只读复核建议补齐“实际POST expectedVersion直接等于Queued SQL ExpectedVersion”的显式断言；`0d44e15bc6884127e977b1e2622737ac7748f6dc`增加该断言与1个正负专项，冻结其102源码闭包后，独立核验一次退出0，未重做实际捕获、替换、登记、Worker启停或清理。实际与验证SHA分别记录，不冒称实机运行已包含后加断言，也没有独立失败记录被覆盖。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/validate_container_acl_source_plan_replaced.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/verify_container_acl_source_plan_replaced.py container-acl-source-plan-replaced-4bdfca63dbe843a0a55a3c6c30130d19
```

私有证据目录`deploy/artifacts/container-acl-source-plan-replaced-4bdfca63dbe843a0a55a3c6c30130d19/`为root:root/0700、文件0600，不入Git。实际恢复意图/预检Job `bd459f83-85eb-c230-42c2-44ba2b4e44c5`，新普通捕获`07b85960ad9c429a2658b3932aa1f97f`；执行ID始终null。

## 实际边界和结果

新无网络/非root/只读根容器、独占目录及临时管理员经正常Authentik→Vue完成一次普通access ACL修改产生新来源。父门禁实际验证2097159字节、九字段/两ACL/普通属性及SQL/Root Completed/HDD后，页面正常选中此来源。普通捕获确实修改了测试目标ACL，后续保持均相对此捕获后基线，不称整场零修改。

确认其它活动任务为0，再仅停止原Worker；正常页面唯一恢复登记实际202/Queued，no-store，原captureId/摘要、instance/path和目标版本不变；请求发送时就保存原Subject/key/body用于异常查账，未注入身份或拦截响应。Queued SQL完整来源/命令/身份/期限已冻结，无plan_json/hash、确认、执行ID、RootStarted、回执或当前恢复HDD前像；实际请求key推导的ID等于SQL实际ID。

然后沿用[准确替换工具](CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)：新来源原件保留在准确隔离inode，另一临时inode仅createdAtUnixMs+1并重算正确规范digest，完整写入/fsync后NOREPLACE发布。旧摘要实际409/AGENT_VERSION_CONFLICT、新摘要实际200，完整快照仅时间/摘要不同；原key200仍返回原Queued/原来源。启动Worker前完整SQL仍等于原Queued，目标实盘完整清单及其它144份HDD元数据字节九字段保持。新摘要可读不等于新计划采用它。

再启动**同一ID、同一镜像Worker**，原规划实际Failed/CONTAINER_ACL_RESTORE_SOURCE_CHANGED，原来源/命令/身份/幂等键/trace/期限不变。plan_json/hash及Ready期限、确认、执行ID、RootStarted/回执/ACL确认/发布版本均null，原规划Job有准确planning安全摘要；没有Root Plan/Discard/Apply记录或当前恢复HDD前像。此处ACL确认是未证明、历史回执未取得，不能称Aborted/false或已执行。精确撤回QA替换inode、归档其字节后清理，只还原原来源inode；原摘要读取200并恢复完整原快照，页面仅查询原失败任务，SQL终态、物理源和原HDD保持，不另建任务、不延长原期限。

## UI、权限、原件和恢复

- 原320×600暗色弹窗可见“ACL修改未完成”及来源变化/查询原任务，任务中心原Job在320暗色和1440亮色均显示“预检失败，未进入ACL修改执行”。目标对象/模式/无需变更/ACL已确认/绑定均未证明、回执未取得、历史和发布否，禁止误呈现“执行失败”；无通用取消/重试按钮，无水平溢出。5张真实PNG，已人工查看恢复弹窗、暗色关键事实和亮色预检标题3张，其余由实际页面断言及独立PNG核验覆盖，不称逐张人工查看。
- 真实请求恢复登记1、确认0、取消0、内容PUT0、Job写0；原主体恢复审计恰2条，plan-accepted Accepted与plan-result Failed，原元数据/来源哈希对应。原上下文正常用户菜单BFF/Authentik双端退出；另一个新普通身份正常HTTP登录两路403、匿名两路401，普通身份正常双端退出，不冒称新增普通身份Vue拒绝页。
- 旧144份HDD元数据全部字节及九字段保持，规范目录总145，仅新增本轮普通捕获1份，其原Root/HDD永久保留；没有恢复执行前像。QA替换只归档后准确清理，不当产品删除能力验收。新原件字节及前8字段保持，ctime从1793742336034303209变为1793742372921038222，目录时间随移动变化，不称新原件九字段全不变。源与HDD同device，不算异盘恢复或整机NVMe/HDD布局验收。
- 独立核验实际SQL/原Root-HDD/替换归档、原来源恢复、无新计划/执行/Root/HDD、请求/页面/正常退出/权限及准确清理；新增断言直接核实原请求目标版本等于Queued SQL，拒绝重复JSON键。父门禁和独立各自使用source-plan-replaced字节生成规则并核SHA，清理前完整字节由父门禁实际验证，独立不声称重读已删除源。
- 源/容器/临时身份准确清理，旧失败树未动，活动任务/租约/屏障/终端/路径锁归零。11核心healthy，全部ID/镜像保持，仅同一Worker StartedAt改变，其余10核心完整保持；Agent2178169/维护2178164及28062da来源保持，7配置和完整发布账本保持，开发NOPASSWD及产品最小授权不改。root只读Git使用GIT_OPTIONAL_LOCKS=0，收尾索引所有者仍1000:1000；后加断言的正常提交与其后的只读索引保护检查分列，不误称整个期间索引字节未变。

异常恢复复用上轮[原请求查账与未成日志stage保留/告警](CONTAINER_ACL_SOURCE_REGISTER_REPLACED.md)：未知状态不擅自取消或删源，只有原失败终态/无新Root-HDD、完整源不变且无待恢复标记才允许准确清理；不删除未证明暂存对象。本场没有异常注入，不把工具离线失败路径当实机故障。

## 验证与未完成范围

最终163相关root离线检查通过、0跳过：本场9专项（初始8+后加1）、16替换工具、22隔离、其它各阶段来源/恢复证明及8范围；严格Python/Node语法通过。首次CI查询TLS超时，重查前批029192d/41723289571仍in_progress，5项基础成功/6浏览器分片待；新提交终态另取，不称CI全绿或全量前后端重跑。CI仅接离线检查，不自动调用实机故障门禁。

四个缺失来源边界与四个同capture“时间+1/自洽摘要”替换边界已有分别实机证据，不代表其它ACL内容变化、依赖暂不可用/恢复、原期限、原三态回执优先或完整来源矩阵通过。递归ACL/恢复、可恢复删除/长期清理、P0-05维护发布连续性及其余59项仍待；当前主项和阶段不升完成，不自动回滚dev。[完整来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。
