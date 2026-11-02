# 递归普通权限：真实组撤权与账号停用后的部分结果保留

2026-11-02。产品保持`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/协议1.49；本批只增加验收工具和记录，没有发布或自动回滚。验收源码冻结`3c76ecc436fa1071cecce5aedccd9924b9597c8a`，20份运行依赖源码逐字与Git blob/SHA对账。两种场景使用各自新建的准确自有容器、目录树及临时Authentik身份，**两例首次完整运行及各自独立只读核验均通过**。既有取消、Worker强杀、目标消失及Agent重启不重跑。

## 实际切点与唯一身份变更

正常可见Chrome/Authentik登录→已部署Vue预检/唯一确认→永久SQL/双Job/Worker→已装签名Agent/helper。每棵树为16文件+2目录，每文件2097159完整字节、普通user属性、混合UID/GID和纳秒mtime；原文件0640/目录0750，预检目标0600/0700，18项预计修改。容器无网络、非root1000:1000、只读根、限资源，仅绑定本批唯一HDD目录。

只为在确认后布置观察器，短暂停启原Worker容器，其他任务/终端/锁/屏障均为0；不强杀Worker、不重启Agent或维护服务。原helper按准确pidfd、固定argv、容器root FD和原operation lock FD绑定并暂停，第一项真实chmod和Completed子回执已经发生；聚合仍Applying、finishedThrough=0/startedThrough=0。保存SQL/原Root字节/全树完整内容和九字段，没有产品故障钩子或SQL/时钟/缓存写入。

只调整本批生成且严格匹配用户名、Subject、数值PK、启用状态和管理员组的一个临时Authentik账号。使用既有`OwnedAuthentikRevocation`：再次读取原对象后，只发一次精确PATCH，再GET确认；响应丢失不授权第二次变更。移组仅写`groups=[]`，停用仅写`is_active=false`并保持组不变。凭据只在内存/子进程环境中，不进Git、截图或日志。没有改变真实用户或全局组配置。

| 实际结果 | 移出管理员组 | 停用账号 |
| --- | --- | --- |
| Authentik变更确认UTC | 10:42:59.888016 | 10:45:01.720061 |
| 原正授权快照到期UTC | 12:42:20.190524 | 12:44:23.4554867 |
| 变更时原快照剩余秒数 | 7160.303 | 7161.735 |
| 产品结束原helper的观测上界 | 4.116秒 | 4.113秒 |
| 原任务GET | HTTP403 | HTTP403 |
| 原会话`/auth/me` | HTTP200、isAdministrator=false | HTTP403 |
| 原页面重载后的实际落点 | `/forbidden`，“没有管理权限” | `/login`，“使用工作站账号登录” |

原快照还剩接近两小时，明确不是自然过期。两例原页面仅一次登记/确认，**取消POST和内容写入均为0，SQL cancel_requested_at_utc为空**；由产品实时身份检查结束helper，验收器未强杀。原容器实例保持至最终准确清理，没有以目标消失诱发撤销。

## 结果、页面及不同管理员处置

两例均按原intent/执行Job/Subject/实例/请求和执行计划对账为**Partial：1完成核对、1实际修改、1中止、16未开始、0待证明**，互斥计数总数18，SQL/Root/receipt/HDD一致且保护保留。撤权已经发生实际修改，不能套用单文件“零修改、Canceled/Aborted”的结论。

切点树、helper退出后树、Worker观察恢复后的树完全相同；每个文件完整字节、inode、UID/GID、mtime/user属性保留，仅第一项预期模式和必要ctime变化。已完成项不重做，未开始项不续做。每例HDD聚合1份及Completed子项历史1份、Root两个子回执Completed/Aborted核验，不生成16个未开始项的假历史。

原真实320×600暗色页面已在撤权前证明递归任务引用存在；撤权后验证旧管理API拒绝、任务region消失、sessionStorage递归引用清空及上述页面落点，无横向溢出。此处`normalDualLogout=false`，**不把撤权/账号停用记作正常双端退出验收**；浏览器正常关闭，原临时用户随后确实删除。

另一位新建管理员通过正常可见Authentik登录，在320×600明色异常处置页定位同一Partial任务，先“暂不处置”（POST0、原保护/SQL/Root/文件不变），再唯一明确确认（HTTP202）。原执行Job为Failed、intent为Acknowledged，acknowledged_by是新Subject，原actor和永久事实保持。此批验证了恢复页Partial状态、确认按钮和处置结果；五类完整计数由SQL/Root/HDD/物理对账，**不冒称本批对恢复页五个dt/dd另作逐项断言**。

追加已装固定privileged helper迟到apply原请求，仍Partial/replayed，原Root字节及全树九字段/完整内容不变。这个追加探针**不是单独签名UDS RPC**，不借主业务链签名外推。没有恢复旧模式来伪装修改未发生。

## 独立核验、清理及验证范围

- 两个主进程退出0、verifiedFixtureRemoved=true。每例均先完整核对源树后准确unlink/空目录rmdir、准确CID清理；永久SQL/Root/HDD保留，原用户和恢复用户均实际删除。
- 两个独立只读进程重读实际SQL/Root/已attest HDD，与私有原始字节一致，重算计划/清单/子计划/版本/历史/outcome域摘要，核对原/新Subject、撤权确认和未到期时间、停止边界、迟到回执与准确清理。独立进程不声称重新读取已删除源字节；DOM和storage断言来自真实浏览器回执，另有实际PNG。
- 每例5条递归哈希审计，唯一partial-acknowledged是新Subject，其余是原Subject；details只含intentId/containerId及重算instanceHash/pathHash。共10审计、8张真实320×600 PNG；目视复核两种原身份落点。
- 每次独立核验11核心healthy，8数据库平台容器ID/镜像/状态/StartedAt不变；API/Gateway和Agent/MaintenanceHost不重启。原Worker仅预检布置观察器时按原容器停启。11个已装helper逐字匹配产品0dede08。第二例开始前第一例已清理并独立核验，未并行改变核心。
- 51项关联/隔离边界测试通过、0跳过；记录落盘后8项范围检查通过，206份MD的1248个相对链接零缺失，Python/Node语法及diff检查通过；没有修改产品源码，不借用旧后端/前端全量或产品CI作为本批结果。最初检查命令误写两个不存在的测试模块（2个导入错误），改用实际文件名后发现16项需root跳过，再以隔离root完整51项全部通过；该命令错误不是产品或实机门禁失败。
- 源与HDD历史device均2049，只计同设备元数据留存，不计NVMe业务/异盘灾备或HDD故障。user NOPASSWD授权保留，产品Agent权限未扩大；无主机重启、磁盘填满、真实通知或自动回滚。

## 私有证据及后续

| 场景 | 私有证据目录 |
| --- | --- |
| group-removal | `deploy/artifacts/container-recursive-permission-faults-c83562c9548e44b894c70faffeb76d14/` |
| account-disable | `deploy/artifacts/container-recursive-permission-faults-1c860580db254746a0b55dbfc043b9d6/` |

目录root/0700、原件0600且不入Git，包含baseline/20份冻结SHA、ready/原正快照期限、bound-helper/cut-root、pre-authority SQL、physical-cut/after-helper-exit/partial/final、authority-change、原/新浏览器回执、SQL/Root/HDD、history/late/audit/installed-core/verified/independent-final及每例4 PNG。无登录凭据截图。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 group-removal
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 account-disable
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-c83562c9548e44b894c70faffeb76d14
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-1c860580db254746a0b55dbfc043b9d6
```

已有回执，不为展示重复前两条真实身份变更。后两条为只读核验，未来若宿主再次重启或产品换版，应保留本批进程/版本证据，不重写历史。

本批仅签收原实例保留、首项实际修改后的组移除和账号停用组合。仍需原实例保留时正常退出、其他Agent/Worker/回执/HDD/路径与跨盘切点、其他写入类型及首次登记，再递归所有者/ACL/可恢复删除/长期清理、P0-05手动回退来源与其余59项/UI/C#酒馆/供应商/AI Live2D。其他模块实时撤权、完整文件功能和整项交付不能据此签收；17项移出不计完成，master未交付。
