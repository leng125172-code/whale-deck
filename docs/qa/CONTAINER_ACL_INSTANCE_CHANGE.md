# ACL写后同CID实例变化：旧绑定Unknown与当前管理员明确处置

> 后续候选：caa9c64区分预检失败与执行失败，新增旧版本预检拒绝的独立真实门禁，目前只完成离线/生产预览，未部署或运行真实新场景。见[候选与边界](CONTAINER_ACL_STALE_PREFLIGHT.md)。以下实例变化实机原正文保持，不重复已签收操作。

2026-11-03。QA固定`d2ca69de3524fa907e17a4ac54906fc19c7abdc1`，proofVersion4、33份来源/11份安装模块逐字节核对；实际产品继续`447070ea6af1e2c5ea16c76c7d6c9ed476c7c808`、协议1.51。**新独占资源的真实常规故障整链与独立只读复核通过；首轮失败保留，不记整体首轮通过。** 本批仅修改QA与记录，无新增产品部署、EF/schema、协议或安装helper修改。59主任务继续部分实现，master未交付。

## 首轮真实操作已发生，完整验收失败

b0f159a使用第一套新资源，在ACL实际写入、Root Applying且尚无确认回执时调用准确自有容器的restart。命令成功返回，但本机Docker29.9.2将`--time`弃用提示写入stdout，使“完整stdout必须等于CID”断言失败。该轮未保存完整前后inspect对，故不补造已证明的实例转换；原浏览器等待退出信号超时、身份清理后的取消403，原上下文正常双端退出也未证明，不能计通过。

失败回收使用当前管理员正常BFF明确确认，`failure-fallback.json`明确`normalBffAcknowledgement=true/gatePassed=false`。只在终态、保护已释放且实际写后清单匹配后清理准确自有资源；Root/HDD保留，未重做旧修改或重启旧资源。原件root私有`deploy/artifacts/container-acl-unknown-5cd563da8d774689b66e9439506b6461/`，17文件，无verified.json；主日志`/tmp/wd-acl-instance-change-real.log`保留。

只读`docker restart --help`/旧参数加`--help`证明支持`--timeout`及弃用提示stdout行为，不再操作容器。d2ca69d改为本机支持的`--timeout 1`，在结果断言前保存单次请求、响应和前后白名单inspect；拒绝重复尝试标志。完整Env/私密标签不归档。离线新增mock先因错误inspect形状失败，修正为原生数组后通过；不代表实机失败被改成成功。

## 新资源真实切点与实例变化

第二套新资源两容器/两源均为非核心、非root1000:1000、无网络、只读根和唯一自有HDD bind。正常可见Authentik登录→Vue登记/确认→原双Job/Worker→Agent。Worker仅在准备期按严格空闲预检正常停启；没有强杀Worker/Agent、修改Root/SQL/缓存/时钟/租约或绕过原身份。

目标两个文件各2097159字节，原UID/GID2100:3100、固定mtime和普通xattr。观察器以准确root/锁FD及pidfd捕获access ACL已经改变、ctime增加、Root Applying而aclConfirmed/actualIdentity=null、公开version空、原HDD已验证的切点。原plan仍保留修改前身份/ACL；`cutPhysical`另存完整写后清单，不能从null推断未修改，也不将已写事实回滚或继续执行。

只对本案准确容器调用一次`docker restart --timeout 1`。前后均验证所有权：CID/Name/Image/Mounts/非敏感Config/安全HostConfig完全相同、Running=true；PID4044343→4047348，StartedAt由`2026-11-03T01:47:31.078530835Z`变为`2026-11-03T01:48:20.885065826Z`。源清单与新PID的`/proc/.../root/work`清单均等于cutPhysical。旧instanceId/计划/执行ID/第一回执及MAC不重绑定。前后请求、响应、严格转换和确认后inspect分别保存，未生成removed-owned.json冒充目标消失。

原上下文随后正常面板/身份双端退出；产品在重启起点后4.605秒结束绑定helper，QA未杀helper。pidfd观察先于容器重启，**不声称helper一直存活到退出才停止**。原身份在正常退出后准确删除，另一管理员具有不同Subject。

## Unknown、两次真实摘要与发布保护

旧实例已失效，产品原Root/SQL为Unknown、aclConfirmed=null、publishedVersion空且保护保留；源已写ACL保持。不同当前管理员通过正常Authentik/BFF登录，320×600暗色恢复页先暂不处置，原行/保护不变。只读原规划与执行Job的15字段SQL/API摘要一致；实际详情完整显示“未证明”、Unknown历史与绑定无效，无通用取消/重试。

唯一真实页面确认处置带原ID/版本及有效CSRF/Origin。原意图Acknowledged、原执行Job Failed，确认审计归当前管理员；原主体/登录绑定/实例/计划/执行/第一回执/历史/ACL null和Root/HDD raw不变。再次只读任务详情证明Acknowledged摘要与原Unknown历史仍分开，字段完整入视口；每次标题/事实各一真实PNG。明确处置只释放保护，不证明变更成功或恢复。

另一容器目录原任务四次实际自然延期，版本`[2,4,6,8]`，四个不同next-attempt，等待目录/Root未提前出现。只有上述明确处置之后，同一原目录ID才实际Succeeded并在Vue视口可见。确认后原容器仍为新实例，完整写后清单保持；迟到旧ID/owner/plan固定helper Apply仍Unknown/null/replayed，不重写原Root/HDD/源，不冒称新增签名RPC。恢复管理员原上下文和额外HTTP会话均正常双端退出。

## 独立核验、证据保存与范围

实际3Job/7审计、1永久ContainerAclMetadata历史。源与HDD同device，ACL元数据留存不是文件内容备份或异盘灾备。父观察器在清理前证明完整内容/九字段/两类ACL/xattr与写后清单相同，准确清理两个自有容器/源；Root/HDD永久保留。独立新进程只读重验冻结来源、实例前后/确认后记录、原Root/HDD/SQL/回执、两次真实摘要、自然延期/明确处置/迟到Apply和清理；不声称清理后重新读取已不存在的源字节。

独立结果：`independentReadOnlyVerifier=true`、`sameCidInstanceChanged=true`、`rootUnknownUnchanged=true`、`originalHddUnchanged=true`、`fourNaturalDeferrals=true`、`exactSourcesAndContainersAbsent=true`、`temporaryIdentitiesAbsent=true`、`coreHealthy=11`。Agent3947643/维护3947551及447070e来源保持，除准备期Worker正常停启外核心不重建，无活动任务/终端/锁/屏障/租约。开发sudo NOPASSWD保留，不改密码/产品Agent授权，不自动回滚dev。

成功原件root:root/0700目录`deploy/artifacts/container-acl-unknown-5dabf9644dc34924a182160cfce1f408/`，JSON/日志/9实际PNG均0600，50份文件SHA256索引（索引自身另计）。含parent-final.log、independent-final.json及counts-and-storage.json；原主/独立日志`/tmp/wd-acl-instance-change-strict.log`、`/tmp/wd-acl-instance-change-independent.log`保留。只复制三张原图到受限忽略目录供目视，无图像编辑或合成。

22 Unknown边界、40原生root故障边界、11浏览器证据和8范围检查共81项通过，Python/Node语法与文档检查另记。先前复跑浏览器证明用包路径触发ModuleNotFoundError，改既有discover入口后11项通过；补充计数读取先因字段/表名误用失败，修正为既有operation_jobs与快照实际键后只读成功，不改产品库。未重新跑全量后端/前端，不将离线mock或生产预览当真实身份文件验收。447070e CI41557092143查询仍4路成功/前端运行中，未取得整条成功终态。

本批仅签收“写后同CID运行实例变化、有Unknown回执、不同管理员明确处置与跨容器目录”。其他共享写入/磁盘/路径组合、无回执或不可信绑定/Superseded/Failed等摘要、ACL递归与历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D仍需继续。前批[目标消失](CONTAINER_ACL_POST_UNKNOWN.md)、[Agent重启](CONTAINER_ACL_POST_AGENT_RESTART.md)保留原版本证据，不合并成完整权限验收。

文档复核：8份变动MD的769条相对链接无缺失，目标第1节与d2ca69d逐字一致，上批目标消失QA原正文逐字保留。项目图谱事件`whale-deck.container-acl.d2ca69d.instance-change`已核对projectId/key/eventId回执并移入synced；阶段仍active，不代表59项完成。
