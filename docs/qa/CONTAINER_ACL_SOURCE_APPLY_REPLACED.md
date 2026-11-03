# ACL恢复：首次Apply前有效来源替换

2026-11-04，仅新增签收已确认Pending、首次Apply前同一captureId被另一有效、自洽记录替换。工具profile6/source-apply-replaced仅针对新资源正常捕获的来源；已有缺失和Ready确认替换记录保留，不重放。产品仍28062da/协议1.52，无发布、宿主重启、SQL/缓存/系统时钟修改。

## 冻结与真实结果

运行前冻结`fd23d9347b6109fd378fbbda010ada1e25b86993`、92个Python/MJS源码闭包和13个安装helper，干净树核对通过。以下两次实际命令均退出0；独立核验只读，不重做真实写入。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/validate_container_acl_source_apply_replaced.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/verify_container_acl_source_apply_replaced.py container-acl-source-apply-replaced-b1a3571880df493d9792276ce152e7b5
```

原恢复意图`84f1a03d-198d-f073-f15a-c7a261cbd67a`，执行`ed448b16-34c3-e7c2-631d-73702316e0cc`，新捕获`056f436d789f52070b108abd29069398`。私有证据`deploy/artifacts/container-acl-source-apply-replaced-b1a3571880df493d9792276ce152e7b5/`为root:root/0700、文件0600，不入Git；包括原SQL/Root/HDD、替换归档、读回、正常退出/权限、5张实际PNG及独立回执。

新无网络、非root、只读根容器和独占目录、新临时管理员经正常Authentik→Vue完成一次普通access修改产生来源；父门禁实际核对2097159字节及九字段/两ACL/普通属性、SQL/Root Completed/HDD。普通捕获确实修改了测试目标ACL，后续保持均相对此捕获后基线，不称整场零修改。页面选源并预检Ready后，确认只有本任务活跃，再仅停止原Worker；原Vue确认一次实际202/Pending，原执行Job已排队、RootStarted/回执/当前HDD前像均无。

随后沿用[准确替换工具](CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)：原件保持在准确隔离inode，另一个临时inode只把createdAtUnixMs加1并重算正确digest，完整写入/fsync后NOREPLACE发布。旧摘要实际409/AGENT_VERSION_CONFLICT，新摘要实际200且完整快照仅时间/摘要不同；原key实际200返回原Pending/原来源。此时完整SQL仍等于原Pending，目标实际清单及其它142份HDD字节九字段保持。新摘要200仅证明记录真实有效可读，不代表原业务采用它。

再启动同一Worker，原执行实际Failed/CONTAINER_ACL_RESTORE_SOURCE_CHANGED，原Root为Aborted/aclConfirmed=false，actualIdentity空、RootStarted始终空、没有当前恢复HDD前像或发布版本；SQL/回执及摘要格式/15字段Job摘要与原ID/来源/计划/执行hash及期限核对。目标完整物理清单保持。精确撤回QA替换inode、归档其字节后准确清理，仅还原原来源inode；来源原摘要重新200，页面仅查询原失败任务，无第二次确认/取消/通用重试。迟到**固定Root helper**同原ID Apply仍返回Aborted、replayed=true、aclConfirmed=false，Root字节、SQL、文件和HDD清单不变；这是直接Root围栏复核，不冒称额外签名RPC或Vue Apply。

## 页面、权限、审计和原件

- 实际320×600暗色恢复弹窗可见“ACL修改未完成”，原ID在任务中心320暗色和1440亮色均显示执行失败、Aborted、ACL已确认否、绑定/历史/发布否及SOURCE_CHANGED，无通用取消/重试写按钮，无水平溢出。5张真实PNG，已人工查看恢复弹窗、暗色关键事实和亮色任务标题3张，其余由真实页面断言及独立PNG检查核对，不声称逐张人工查看。
- 请求为恢复登记1、确认1、取消0、内容PUT0、Job写0；原主体恢复审计恰4条：plan-accepted、plan-result Ready、confirmed、result Failed。原上下文正常用户菜单BFF/Authentik双端退出；另一个新普通身份正常HTTP登录两路403、匿名两路401，普通身份正常双端退出，不冒称新增普通身份Vue拒绝页。
- 旧142份HDD元数据全部字节及九字段保持，规范目录总143，仅新增普通捕获1份，原捕获Root/HDD永久保留；没有恢复执行前像。QA替换仅归档后准确清理，不是新增历史或产品删除功能验收。新原件字节和前8字段保持，ctime从1793740688665196063变为1793740734875124769；目录时间随移动变化，不称新原件九字段全不变。
- 独立只读核验重新读取真实SQL、Root/HDD、替换归档、原件和审计/页面回执；独立数据生成域明确为source-apply-replaced，与父脚本通过正负一致性回归。完整源字节由父门禁在准确清理前实际检查，独立核验不声称读取已经删除的源字节。
- 源/容器/临时身份准确清理，旧失败树未动，活动任务/租约/屏障/终端/路径锁归零。11核心healthy、ID/镜像保持，只有同一Worker StartedAt改变，其余10核心完整保持；Agent2178169、维护2178164与实际28062da来源保持，7配置和完整发布账本保持，NOPASSWD授权不改，产品最小权限不扩大。root只读Git禁用可选锁，前后索引所有者1000:1000、inode/模式/字节SHA保持。

## 离线验证与尚未签收

142项相关离线检查以root执行通过、0跳过，包含新6项读取/冻结来源/独立数据生成规则检查，替换工具16、隔离工具22及旧故障证明与范围检查。普通用户首轮跳过38项权限测试，随后已root补跑；另一次范围测试文件名错误未收集，已按test_scope_baseline.py补验。不称前次跳过或空收集为通过。严格Python/Node语法通过。CI仅接入离线测试，不在CI自动调用实机门禁；前批d420a4f/41718877997本次最后查询仍in_progress，5项基础及浏览器4/6成功，其余分片待，不称全绿或重跑全量前后端。归档检查命令曾因多写一个括号在Python编译阶段失败，无文件/真实操作生效；修正后仅重做归档核对通过，未重放本场。

不扩大安装/sudo/RPC，不改产品、SQL/缓存/时钟或旧历史，不自动回滚dev。源与历史同device不算异盘恢复；独立核验不声称重读清理后的源文件。当前59项不升完成，后续[来源矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)、递归ACL、可恢复删除/长期清理与其它目标继续。
