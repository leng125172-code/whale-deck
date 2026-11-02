# 容器ACL：执行前已留历史的进行中取消与正常双端退出

2026-11-03。运行六组件仍为 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，本批没有重新发布。真实门禁冻结 `78479ec`；后续 `a61d715` 加固失败清理，`e1a3419` 补独立观察器的实际容器健康断言。**两套新独占资源的真实页面、SQL/Worker/Agent、原文件/ACL和HDD门禁首轮通过，独立只读核验通过。** 不重复前批[九个常规场景](CONTAINER_ACL_REAL_UI.md)。

仅签收**已进入Applying业务状态、Root为Preparing且HistoryVerified、ACL系统调用尚未发生**时的两种中断。不是ACL已修改后的回执丢失、Worker崩溃、Agent重启、实时撤权、Unknown或递归恢复验收，不计完整ACL或59主项完成，master尚未交付。

## 实际切点与页面动作

每案单独创建准确随机容器：固定现有镜像、非root1000:1000、无网络、只读根、单一自有HDD bind。只在该目录创建acl-file.bin、acl-directory及existing.bin；两个文件各2097159字节，原UID/GID2100:3100、模式0640/目录0750、固定mtime、普通user.whaledeck-acl属性。original.bin由fixture保留原inode和完整确定内容。目标access ACL为owner6、named user2201权限6、group_obj0、mask4、other0，目标模式仍0640，NoChange=false。

浏览器实际可见Authentik登录→Vue原容器文件→填写条目→生成预检。预检Ready后，只停止现有Worker，正常页面确认一次，得到唯一Pending执行Job，再启动同一Worker容器/镜像。未注入Cookie、拦截响应，未改生产SQL/Root/缓存/时钟或安装测试钩子。

QA观察器以pidfd、固定命令 `/usr/bin/python3 -I /usr/local/libexec/whaledeck-container-acl-transport.py apply`、root身份、容器root FD和原任务0600空锁FD同时绑定进程。计划必须与SQL完整字段及规范摘要相同；精确核对父inode、单层acl-file.bin、九字段原身份、access/default ACL、普通属性摘要后才SIGSTOP，并在暂停后重新核对。只操作本案helper；若错过切点就失败，不能用已完成操作替代。

| 实际场景 | 可见操作与产品行为 | 原任务与物理终态 |
| --- | --- | --- |
| cancel | 320×600暗色；先继续等待（取消POST0），再明确请求取消（POST1）；产品约5.433秒终止绑定helper | Canceled/CONTAINER_ACL_CANCELED，原Root Aborted，AclConfirmed=false |
| logout | 1440×600亮色；关闭原弹窗与详情，从原用户菜单正常退出，取消POST0；产品约5.520秒终止绑定helper | 同为Canceled/Aborted，cancel_requested_at_utc为空，原身份正常双端退出 |

两案的终止时间均由动作发出前启动的pidfd观察线程测量，限15秒；QA未SIGKILL helper，finally只恢复/关闭自己的pidfd。单案注册POST1、确认POST1、内容PUT0。取消案可见原终态后再从同一上下文正常退出；退出案使用原上下文的顶层logout POST302/303、实际供应商end-session导航、BFF401、Authentik401/403与最终/login，不用删账号或撤权伪装退出。临时身份只在核验后准确删除。

## 原回执、源文件、HDD和清理

两个意图/四个唯一Job，九条实际审计：plan-accepted2、plan-result Ready2、confirmed2、cancel-requested1、result Canceled2；结果审计保留原Subject。两案planJob Succeeded、executionJob Canceled、已处理Outbox0，原RootStarted/执行ID/历史证明仍在，完成后屏障及租约释放。**这与Ready取消无执行Job、无HDD历史的前批场景不同。**

每案完整读取两个文件的所有字节、九字段inode身份、两类ACL及普通属性；整个清单与原始清单完全相同，目录既有子项不变。原Root Preparing/HistoryVerified切点、最终Aborted/aclConfirmed=false、SQL原不可变Receipt及其MAC摘要、计划/实例/原inode/ACL/执行身份逐项绑定。每案一份实际 `ContainerAclMetadata` 永久HDD元数据，attest只读核对原件，并重算 `whaledeck-container-acl-history-v1` 摘要。没有重新Apply、恢复ACL或删除Root/HDD。MAC由产品读取路径核验，QA未读取密钥另算MAC。

父进程物理证明通过后只删除准确自有文件，目录成员删除导致时间变化另行按同inode/模式/属主/ACL和空目录核对，之后fixture仅清理原original.bin和本案准确CID。两源目录/CID与临时身份最终均不存在。独立新进程再次读取原SQL/Root/attest HDD、逐字节比对原件与冻结依赖摘要，并核对原切点、父进程物理信号及准确清理；**不声称清理后重读了已删除的源字节**。

只有Worker按门禁需要正常停止/启动两次；其原ID/镜像保持，StartedAt按正常重启变化，其余10核心ID/镜像/状态/StartedAt不变，8数据库平台容器未动。最终实际docker inspect为11 healthy；Agent PID3122630、MaintenanceHost PID3122598仍active，实际exe --build-info同源633a2ca。最终全局Job/终端/主机锁及各容器活动/屏障/租约0。user NOPASSWD、密码和产品最小权限未改，无dev自动回滚。

## 测试、失败与边界

初冻结10项观察器拒绝测试和8项范围通过。复核后发现：若浏览器收到202但登记信号丢失，旧失败清理可能因未知ID放行；已改为浏览器启动过却无法证明任务ID时保留源，SQL活动/Unknown/屏障/任一租约/非终态Root/源清单不符均不删。浏览器等待超时记录未通过并保留源数据，关闭自己的浏览器。新增4项清理边界测试，最终**14专项+8范围=22通过**，Python -W error及Node语法检查通过，已接CI。成功实盘门禁采用78479ec，该失败分支未触发；不把后续测试加固说成已重新执行真实写入。

初始本地脚本有重复变量和正则转义语法错误，均在冻结/实机前修正；一次不存在的scope测试文件名产生0tests，随后改为实际test_scope_baseline.py并执行8项。资料定位中小写文档路径及历史CURRENT_GOAL.md读取失败后按rg实际大写文件定位，未据此重建目标或覆盖历史。**真实两案主门禁首轮通过，独立核验首轮通过；增加健康断言后只重跑只读核验，没有重复ACL操作。** 收尾范围检查另发现元数据漏了标准措辞“17项移出不算完成”，已恢复原句，未放宽测试；重验8项通过。本轮没有重新跑全量后端/前端/生产预览或声称远端CI已全绿。

实际3张PNG：cancel进行中/终态均320×600暗色、logout进行中1440×600亮色。已目视检查三张，320×600查询/取消页脚在视口内，mask有效权限与原/执行身份分列，终态为已取消。logout后的页面由正常退出helper断言，不虚构该页面截图。

证据为root私有0700目录 `deploy/artifacts/container-acl-faults-1a9163ceeabd4db9b5b212cf2f708957/`，45份原件的SHA目录及JSON/日志/PNG均0600，含冻结来源、两案baseline/原切点/绑定helper/SQL/Root/HDD/物理终态/浏览器信号、independent-first与independent-final、final-summary。凭据仅内存/隐藏环境，不入Git、日志或密码截图。日志 `/tmp/wd-acl-faults-first.log`、`/tmp/wd-acl-faults-independent.log`、`/tmp/wd-acl-faults-independent-final.log` 留存。

剩余：ACL实际系统调用后取消/丢回执、实时组撤权/停用、Worker强杀/自然租约与Agent重启、目标/实例变化Unknown及其他共享写入延期/明确处置组合，ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D。单对象原ACL元数据不等于内容备份或异盘灾备；17项移出不计完成。

文档终检：227份Markdown、1393处相对链接无缺失；目标第1节逐字不变，前批常规QA原始正文逐字保留，59项/17移出口径不变。项目图谱事件 `whale-deck.container-acl.78479ec.pre-syscall-faults` 已取得projectId/key/eventId一致回执并移入synced，阶段保持active。

远端同步状态：本批实机与文档已本地提交；2026-11-03首次push及一次重试均报 `GnuTLS, handshake failed: The TLS connection was non-properly terminated.`，独立ls-remote也同错。当前未取得远端SHA成功回执，不能称已推送或CI已运行。本地工作和图谱成功回执保留，后续先恢复dev同步，不重复两案真实操作。
