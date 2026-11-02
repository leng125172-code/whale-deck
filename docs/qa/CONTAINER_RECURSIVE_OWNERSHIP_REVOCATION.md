# 递归所有者：真实管理员组撤权与账号停用

后续更新（2026-11-02）：新资源的目标消失Unknown/不同管理员处置与另一容器目录四次自然延期、明确释放后原ID真实发布及独立核验已通过，见[后续Unknown与跨写入证据](CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)。以下撤权批次正文逐字保留，原“Unknown/跨写入仍待”仅为当批状态；其他组合/ACL/删除仍待。

2026-11-02。产品保持`b1fafedddfa1767d2d573bd94d2748f3cb5e7b44`/Agent1.50，无发布或自动回滚。验收工具冻结`cf291aa7dc7569bb8792d9f162345eb00343a17a`，21份实际依赖按Git blob/SHA核对。两套新独占资源首次完整实机门禁和各自独立只读核验均通过；此前常规、取消/正常双端退出、Worker强杀与Agent重启未重做。

## 准确切点及唯一身份变更

每例通过可见Authentik登录→已部署Vue预检/唯一确认→永久SQL/双Job/Worker→已装Agent/helper。新容器无网络、执行身份1000:1000、只读根、仅准确HDD bind及资源限制；每棵树16文件+2目录，每文件完整2097159字节、普通user xattr、混合原UID/GID、文件0640/目录0750与固定纳秒mtime。目标明确2200:3200，数字属主不等于人类Subject或执行身份。

预检无其他任务/终端/锁/屏障后，为布置观察器短暂停启原Worker容器；没有强杀Worker、重启Agent/维护服务或修改生产配置。原SQL执行ID、原容器instance PID、容器root FD和原operation lock FD、固定root argv/cgroup及pidfd共同绑定原helper。首项实际fchown和Completed子回执已发生、聚合Applying、finishedThrough=0/startedThrough=1、根目录原样，才暂停准确helper。产品须在15秒内结束它；验收器未强杀helper，不以睡眠猜测PID。

只改变当例新建的一个临时Authentik账号。`OwnedAuthentikRevocation`严格验证自有用户名格式、原Subject/UUID、数值PK、启用状态和管理员组，再GET核对原对象；移组只PATCH `groups=[]`，停用只PATCH `is_active=false`且原组保持，再GET确认。一次响应丢失也不授权第二次PATCH。不改真实用户、组定义或全局身份设置；凭据只在内存/子进程环境，未写Git或日志。没有SQL/Root/缓存/时钟/租约编辑、Cookie/响应注入或产品故障钩子。

| 实际证据 | 移出管理员组 | 停用账号 |
| --- | --- | --- |
| 变更确认时原正权限快照剩余 | 7161.151秒 | 7162.322秒 |
| 产品结束准确原helper的观测上界 | 3.951秒 | 4.255秒 |
| 原任务GET | HTTP403 | HTTP403 |
| 原会话`/auth/me` | HTTP200、isAdministrator=false | HTTP403 |
| 原页面重载后落点 | `/forbidden`，“没有管理权限” | `/login`，“使用工作站账号登录” |
| 页面登记/确认/取消/内容写入 | 1/1/0/0 | 1/1/0/0 |

正快照仍近两小时，不是自然过期。两例原容器实例保留至准确清理；SQL `cancel_requested_at_utc`为空，不以正常退出、取消或目标消失代替实时撤权。

## Partial与不同管理员真实页面处置

两例均恢复原操作的Partial：**1完成核对（含1实际修改）、1中止、16未开始、0待证明**。互斥计数合计18，实际修改不重复累加。切点、helper退出后及Worker恢复后的全树九字段一致，不重做已完成项，不续做未开始项或自动回滚。完整字节、inode、普通模式、mtime与xattr保持；仅首项目标UID/GID与必要ctime变化。原Subject/实例/请求与计划/执行Job/Started及签名保持。

原320×600暗色页面先确认任务提示存在，撤权后核验旧任务GET拒绝、当前任务region消失、当前sessionStorage递归所有者提示不含原ID，页面没有横向溢出。这只证明该页重载与该提示的清理；**未验证其他路由、标签页、浏览器历史、localStorage或其他持久缓存的全部旧引用**。原浏览器回执`normalDualLogout=false`，不将移组或停用签成正常双端退出；原临时账号随后准确删除。

另一位新管理员正常可见Authentik登录，在320×600明色异常处置页定位原Partial，逐一核验“已完成核对/实际修改/已中止/未开始/待证明”五个dt/dd及实际1/1/1/16/0；先“暂不处置”（处置POST0、原SQL/Root/全树保持），再唯一明确确认HTTP202。原Job为Failed、intent为Acknowledged、屏障释放；acknowledged_by是不同新Subject，原actor/Root Partial/实际计数/HDD历史不改。新管理员按同一上下文完成正常BFF/Authentik双端退出。

每例原HDD聚合1份、完成子项历史1份及Completed/Aborted子Root回执独立核对；不替16个未开始项生成假历史。清单/计划/版本/历史/outcomeDigest按独立ownership域重算。额外迟到固定受限helper Apply重放原Partial，源完整内容/九字段及Root原字节不改；此探针**不是签名RPC**，不扩展重放结论。

## 独立核验、原件和准确清理

| 场景 | `deploy/artifacts/`下的私有目录 | 主门禁/独立核验 |
| --- | --- | --- |
| group-removal | `container-recursive-ownership-faults-935a969313bf4dcd9edc75be9ea5932b` | Passed/Passed |
| account-disable | `container-recursive-ownership-faults-d113bde06ed44c74833ebd7e797e6977` | Passed/Passed |

独立进程重读永久SQL/Root、attest物理HDD、冻结源码/21依赖、身份变更与原快照期限、原/新Subject哈希审计和实际PNG，核验原件未被处置/迟到重放改写。每例5条哈希审计、2份HDD元数据、5张实际320×600 PNG，共10审计/4元数据/10图；图片尺寸及SHA单独保存，已目视复核两种拒绝落点、五计数和“暂不处置/确认处置并释放保护”按钮。源/CID经完整证明后准确删除，独立观察不声称清理后重读已删源字节。

最终只读检查于2026-11-02T15:25:48.350973Z确认：三类临时Authentik用户名缀均0；活动任务/终端/主机锁、八类容器意图/屏障/租约和递归所有者transient均0。11核心healthy，8数据库平台容器ID/镜像/状态/StartedAt不变，Worker仅观察器准备时按同CID/镜像停启。Agent PID2542378、MaintenanceHost PID2380322及实际b1fafed来源保持。源和HDD元数据device均2049，只计同设备历史留存，不签NVMe业务/异盘灾备/HDD失联。

主实机门禁和独立核验无失败。其后附加只读汇总器首次将内存tuple直接与JSON list比较，触发AssertionError；保存`post-gates-first-observer-failure.json`，按同一字段规范化后仅重读核对并通过，未重复身份PATCH、fchown或页面处置，不把该汇总器错误隐藏为产品失败/通过。`post-gates-identities-and-idle.json`和`screenshots-sha256.json`保存最终证明。目录root/0700、文件0600、不入Git；日志为`/tmp/wd-recursive-owner-{group-first,group-independent,disable-first,disable-independent}.log`。

## 工具修复、离线检查与剩余边界

本批启用Python/浏览器入口的group-removal/account-disable白名单；恢复页五计数逐项DOM断言纳入两种撤权，独立核验要求真实计数回执与第五张截图。56递归观察边界、12临时身份精确变更边界、8范围检查全部通过，Python -W error及Node语法/diff检查通过。没有产品源码/schema/运行授权变更，不冒称本轮重跑旧后端/前端全量；生产真实页面证据与测试接口的生产预览测试分开。 文档更新后8项范围检查首轮因项目元数据将固定约束“17项移出不算完成”缩写而失败；恢复原约束原文后重跑通过，不放宽测试或恢复移出任务。

此前[常规六场景总脚本退出Failed及单独补证](CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)、[同上下文取消/正常退出](CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL.md)和[Worker原自然租约/Agent重启](CONTAINER_RECURSIVE_OWNERSHIP_PROCESS_RESTARTS.md)保持原批次。用户NOPASSWD和密码未改，产品Agent最小权限未扩大，无发布/回滚/主机重启/磁盘填满/真实通知。

下一步仍为递归所有者目标消失Unknown、跨写入延期/释放、其他切点及回执组合，随后ACL、可恢复删除与长期清理、P0-05和其余59项/UI/C#酒馆/统一供应商/AI Live2D。当前门禁只新增启用上述两种撤权；未启用的Unknown或其他模块旧证据不算本功能已验收。P1-11/P2-03及全阶段仍部分实现，17项移出不计完成，master尚未交付。

文档收尾：217份Markdown、1317处相对链接零缺失；原进程恢复QA正文逐字保留（SHA256 `dabbd5aa396160c76e50e7259429ccf97e3f9e15d5b698b420da0632ee5b1932`），仅前置后续说明。当前目标仍59项，整项正式签收数及规划代码量估计未因本批测试增加而上调。
