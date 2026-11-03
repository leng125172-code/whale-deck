# ACL恢复：异步预检前来源丢失实机验收

2026-11-04，仅新增签收**来源正常时登记Queued，Worker领取前该准确来源丢失**。与[Ready后确认拒绝](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)、[已确认后首次Apply拒绝](CONTAINER_ACL_SOURCE_APPLY_MISSING.md)分开记录，不重放前批动作、不扩签首次登记拒绝或来源替换。产品保持28062da/协议1.52，无部署/两宿主重启；仅正常停止、启动原Worker。

## 运行与回执

门禁冻结`4d38a51dd65ccbc8f2527be7744383fc817a774b`，干净树启动，运行前Python/MJS本地静态导入闭包74文件及13个安装helper逐字核对。原导入闭包方法新增显式seeds参数供此独立profile使用，默认行为保留；前批闭包及执行证明11项回归通过。

```sh
sudo -n python3 -u deploy/validation/validate_container_acl_source_plan_missing.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n python3 -u deploy/validation/verify_container_acl_source_plan_missing.py container-acl-source-plan-missing-1233850372ec41488d4b4da5d3b2ab26
```

实机门禁、独立核验均**退出0**。私有证据`deploy/artifacts/container-acl-source-plan-missing-1233850372ec41488d4b4da5d3b2ab26/`为root:root/0700、文件0600，不入Git。恢复意图/原预检Job均为`e3051561-18c9-44e3-3e2a-a29a64d04ecf`，执行ID始终null；本轮新捕获`225f001ce45e0d1d7654c6b03bf285c4`。

## 真实切点与失败恢复

1. 全新无网络/非root/只读根测试容器、独占目录及临时管理员，正常Authentik→Vue完成一次普通access ACL捕获。父门禁核实完整2097159字节、身份/两类ACL/普通属性、SQL/Root Completed/HDD后才放行历史选择。本场普通捕获确实修改测试ACL，后续“保持”均相对该捕获后基线，不称整场零修改。
2. 真实页面完成历史扫描、读取及选源，打开恢复弹窗，在**登记POST前**以私有信号暂停；此时核实全局无活动任务/终端/文件工作，再停原Worker。来源仍正常可读，页面只提交一次登记，返回202/Queued；SQL版本0、原规划Job Queued、单个待发消息，源快照/命令已冻结，没有计划、确认或执行信息。
3. 仅隔离本轮新捕获的HDD规范元数据原件，保留同inode及私有持久日志/目录证明。真实准确历史读取404，原key GET200且与原Queued DTO及来源完全一致；父门禁再核对完整SQL未变、目标未变及原/派生执行ID的Root/HDD记录均不存在，才启动同一Worker。
4. Worker按冻结的Dispatcher顺序先校验来源，来源丢失后原规划Job/业务意图以**Failed / CONTAINER_ACL_RESTORE_SOURCE_CHANGED**结束。未产出Root计划，plan_json/plan_hash/ready期限均null；执行ID、确认、RootStarted、回执、当前恢复HDD前像、发布版本仍无，屏障/租约释放。实际目标完整字节、九字段、两类ACL和普通属性与捕获后基线完全相同；不是只凭一个null布尔值推断零修改。
5. 原来源JSON/摘要、actor/login、容器/实例/路径、幂等键、trace、请求MAC及原规划期限均保持。规划到期仍`2026-11-04T04:30:52.54871+08:00`，实际完成`2026-11-04T04:16:12.460978+08:00`；没有SQL、缓存或时钟修改，没有延长期限或另建任务。
6. 同inode原件精确还原后，实际历史再读200、快照相同；页面只查询原失败任务，不重新登记、确认或取消。来源可读恢复不使已经失败的原预检自动重做。页面和API都没有恢复确认或执行回执。

## 页面、授权与审计

原320×600暗色恢复弹窗显示“ACL修改未完成”和实际来源变化提示。真实任务中心在320×600暗色、1440×600亮色打开**原预检Job**详情，显示“预检失败，未进入 ACL 修改执行”。15字段摘要与SQL/API一致：planning/Failed/access、正确来源变化结果码；kind、模式、noChange、aclConfirmed及bindingCurrent为null，界面显示“未证明”；回执“未取得”，两历史事实和发布版本均false。不能照搬前批执行Aborted的“ACL已确认：否”。

两主题逐项断言，关键“未证明”进入视口，所有值在抽屉横向范围内，无水平溢出；没有通用“取消任务”/“重新预检并重试”控件。5张真实PNG（原恢复状态1、两主题标题及事实各2）保留，已人工复核暗色事实、亮色标题及原状态3张；不是测试接口生产预览。

原浏览器上下文正常用户菜单退出BFF/Authentik，BFF401、提供方401/403。请求只有恢复登记1，确认/取消/内容PUT/通用Job写入均0。恢复审计恰2条：原主体plan-accepted Accepted、plan-result Failed，来源摘要和原Job绑定不变。另一个新普通身份正常HTTP登录，对原恢复及准确历史读取两路403，匿名两路401，普通身份正常双端退出；不声称本批新增普通身份Vue拒绝页面。

## 原件、运行与清理

- 旧139份HDD元数据完整字节及九字段保持；总140，只新增普通捕获1份，没有恢复的Root执行/Discard记录或当前ACL前像。普通捕获原Root/HDD保持，前批Failed/Aborted等原件不改写。
- 本轮新原件字节、device/inode、mode、uid/gid、link count、size、mtime保持，ctime从`1793736938001484169`变为`1793736973011630414`。目录时间随移动变化，隔离目录已准确移除；不称本轮新捕获九字段全不变，也不删除历史元数据。QA源/HDD同device，不算异盘恢复或整机NVMe/HDD布局验收。
- 独立核验重新读取实际SQL、Root/HDD缺席、原捕获原件、完整源码闭包/Git对象、Queued→失败绑定、正常退出/权限、真实任务详情及准确清理。源字节由父门禁在清理前实际核实；独立核验不声称重新读取已删除源文件。
- 本轮源目录、容器及临时身份准确清理，旧失败源树未动，所有任务/租约/屏障/终端/路径锁归零。六组件28062da，Agent2178169/维护2178164不变；11核心healthy，原Worker ID/镜像保持、StartedAt正常改变，其余10核心完整标识/启动时间保持。7项受保护配置和完整发布账本保持，开发user NOPASSWD保留，产品Agent授权未扩大。

本轮8项新证明、11项前批执行/闭包、22项隔离/强杀恢复、28项旧恢复证明、5项确认缺失证明及8项范围检查通过，0跳过；Python/Node严格语法通过。实际新资源首轮执行通过。前批4f9d3b1 CI最后只读查询仍in_progress，不能据此宣布整条通过；新提交CI另取终态，不冒称全量前后端重跑。

## 尚待

首次登记前来源已丢失、各阶段真实来源替换、暂不可用与原期限/原三态回执优先等[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)继续；递归ACL/恢复、删除长期清理、更多跨写入/磁盘组合、P0-05维护发布连续性和其余59项仍未完成。只更新本场异步预检缺失分项，主项及六阶段不升整项完成。
