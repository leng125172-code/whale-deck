# ACL恢复：首次Apply前来源丢失与原执行永久Aborted实机验收

2026-11-04，接续[Ready确认缺失验收](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)，本轮仅签收**已确认Pending、首次Apply前准确历史来源缺失**。产品仍28062da/协议1.52，未部署或重启两宿主；只正常停止、启动原Worker容器，不更换其ID或镜像。没有SQL、时钟、缓存或浏览器身份注入，不自动回滚dev。

## 实际回执

门禁冻结`bbd1b526f5c16d442b150603fc524cf3be5d0c9a`，干净树启动。新增运行前本地Python/MJS静态导入闭包核对，实际69份源码逐字匹配该Git版本，13个安装helper匹配产品版本；区别于前批42项入口清单的事后补充核对。

```sh
sudo -n python3 -u deploy/validation/validate_container_acl_source_apply_missing.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n python3 -u deploy/validation/verify_container_acl_source_apply_missing.py container-acl-source-apply-missing-f2a9bc49ca9946669048721ed3fee6f9
```

实机门禁和独立核验均**退出0**。私有证据目录`deploy/artifacts/container-acl-source-apply-missing-f2a9bc49ca9946669048721ed3fee6f9/`为root:root/0700，文件0600，不入Git。原意图`a2df7fef-f0db-cc5b-2379-e2ba96525862`、原执行`180077ed-a94f-140f-c30b-0d8eeb6828ee`、新历史捕获`7bd76efc563593fbe8c070c32a32420e`。

## 新资源与准确切点

1. 新无网络/非root/只读根容器及独占目录、新临时管理员，经真实Authentik→Vue执行一次普通access ACL修改以生成新来源；完整2097159字节、身份、两类ACL/普通属性和SQL/Root/HDD核实后，才放行正常页面历史扫描、选源及新恢复预检。普通捕获确实改变了该测试目标ACL；后续“未改变”均相对于捕获后基线，不把整场说成零修改。
2. Ready时核实没有其它活动作业、终端和文件工作，再正常停止原Worker。原Vue确认**202**，SQL原意图Pending、原执行Job Queued；没有RootStarted、执行回执、当前恢复HDD前像或原执行Root记录。原来源此时仍可读，不能把上一场确认409冒充本场执行拒绝。
3. 只隔离本轮新捕获元数据，保留原inode及私有持久日志/目录身份证明；真实历史读取**404**，原key GET **200**且与已确认Pending DTO/冻结来源完全一致。浏览器回传该事实后，父门禁再核对完整Pending SQL未变、目标未变及Root/HDD缺席，才启动同一Worker。
4. Worker通过实际签名Agent链处理原执行，先Inspect、再核实来源；来源缺失后仅Discard原执行。原意图和执行Job终态**Failed / CONTAINER_ACL_RESTORE_SOURCE_CHANGED**，原Root记录**Aborted / aclConfirmed=false**，无actualIdentity、无发布版本、historyMayExist/historyVerified均false、historyDigest为空。业务RootStarted始终null，未开启发布屏障，原租约释放。
5. 终态保持原subject/login、CID/instance/path、命令/计划/来源JSON及哈希、原执行ID/执行MAC和确认/执行期限。执行到期时间仍`2026-11-04T04:14:13.881774+08:00`，实际完成于`2026-11-04T03:59:35.431118+08:00`；未修改时钟或延长期限。目标完整字节、九字段、两类ACL及普通属性与捕获后基线完全相同，不是依靠仅一个布尔字段推断未写入。
6. 来源按日志及准确隔离目录inode还原；真实历史再读**200**且快照相同。随后QA仅对这一个原执行ID调用一次固定Root helper的迟到Apply验证围栏：仍**Aborted / replayed=true / aclConfirmed=false**，Root原始字节、SQL、目标和全部HDD记录均不变。这次迟到检查是受限Root helper调用，不冒称来自页面或新的签名Agent RPC，也不是重新执行业务任务。

## 页面、权限与审计

原320×600暗色恢复弹窗查询同一个任务，显示“ACL修改未完成”。正常API读取同一原执行Job，15字段摘要与SQL一致：execution/Failed/来源变化、access/File/0640/noChange=false、ACL已确认false、Aborted、bindingCurrent=false、两历史事实false、无发布版本。这里已取得可信Aborted回执，不能误标“未证明”，也不能把预检Succeeded当恢复成功。

真实任务中心在320×600暗色、1440×600亮色各打开**原执行ID**详情，逐项验证上述摘要；“ACL 已确认：否”进入视口，所有值在抽屉横向范围内，无横向溢出。没有通用“取消任务”或“重新预检并重试”按钮，没有通用任务写入。5张真实PNG（恢复状态1、两主题标题及事实各2）保存，已人工复核暗色事实、亮色标题和原恢复状态3张；不是生产预览测试接口。

原浏览器上下文正常通过用户菜单完成BFF/Authentik双端退出。请求统计仅恢复登记1、确认1，取消0、内容PUT0、通用Job写入0；来源恢复后没有第二次确认。恢复审计恰4条：原主体plan-accepted、plan-result Ready、confirmed Accepted、原执行result Failed，来源摘要及执行绑定相同。不同普通身份正常HTTP登录对原恢复及准确来源读取两路403，匿名两路401，普通身份正常双端退出；不冒称新增普通身份Vue拒绝页面验收。

## 原件、运行和清理

- 原138份HDD元数据全部字节及九字段保持；总139，仅增加本场普通捕获1份。**没有为恢复执行新增当前ACL前像**，原执行Root Aborted记录永久保留，原普通捕获Root/HDD也保持。
- 本轮新元数据原件的字节、device/inode、mode、uid/gid、link count、size及mtime保持；ctime从`1793735929902168018`变为`1793735976001371782`。目录时间也随移动变化，不声称本轮新原件九字段完全不变。隔离目录已准确移除，元数据不删除。
- QA源目录与HDD元数据同device，这不是跨物理盘恢复或整机NVMe/HDD布局验收。
- 独立核验重新读取实际SQL、Root、HDD与完整导入闭包的Git对象，检查原Pending→失败绑定、原件还原、迟到回执、两真实任务抽屉、审计、正常退出及准确清理。完整源字节由父门禁在清理前实际验证，独立核验不宣称读取已删除的源文件。
- 源目录/测试容器及本轮临时管理员、普通身份均已准确清理，旧失败源树未动。任务、租约、屏障、终端和路径锁归零。六组件仍28062da，Agent2178169/维护2178164保持；11核心healthy，除原Worker的StartedAt正常改变外，全部核心ID/镜像及其它10个StartedAt保持；7项配置和完整发布账本保持，开发user NOPASSWD保留，产品Agent权限未扩大。

本轮11项新证明/导入闭包、5项前批来源证明、22项隔离/强杀恢复、28项旧恢复证明及8项范围回归通过，0跳过，Python/Node严格语法通过。首次严格编译曾发现正则字符串未使用raw字面量，修正后重新编译通过，尚未实机操作时即已解决；实际新资源门禁首次执行通过。CI仅接入离线/语法检查，不自动操作工作站；新提交CI终态另行取得，不冒称全量前后端重跑或全绿。

## 尚未签收

首次登记/异步预检的来源丢失、各阶段真实来源替换、依赖暂不可用续做/原期限及原三态回执优先仍需新资源实机验收；不从本场推断整个HDD或Agent离线行为。更多跨写入/磁盘组合、递归ACL/恢复、可恢复删除及长期清理、P0-05维护发布连续性和其余59项继续，主项与阶段不因一个分项升为完成。详见[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。
