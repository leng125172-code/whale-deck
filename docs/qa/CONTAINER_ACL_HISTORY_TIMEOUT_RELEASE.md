# ACL历史读取超时修复发布与恢复写后撤权实机复验

2026-11-03。产品冻结`b69302b8cb32048038d7dbeed193af205c70fd11`，真实恢复工具冻结`efa900d70bff9ef8b02b22e080f9dc5dd34a8159`。**准确CI五路成功、唯一标准覆盖、六组件核验，以及全新资源的Vue写后移组/账号停用和独立复核均已通过。** 只签本文件列明范围；不把前批失败、HTTP探测或候选测试改写为真实页面通过。

## 标准发布与实际安装

准确候选[CI run41647839607](https://github.com/leng125172-code/whale-deck/actions/runs/41647839607)的backend、installer、native-postgres、native-valkey、frontend全部completed/success。此前前端运行中和网络错误原记录保留，未使用其它提交的CI代签。

唯一标准更新unit为`whaledeck-dev-publish-b69302b.service`，Invocation`c7f46555dedd4db2a84c2e8e8fa46071`。使用`--no-block`启动一次，终态active/exited、MainPID0、Result success、ExecMainStatus0；保存准确Invocation日志和终态后才停止已结束unit，未重复触发发布。

固定维护update重新发布安装Agent/MaintenanceHost、冻结构建四镜像、运行前向Migrator并force-recreate三应用。实际API/Worker/Gateway镜像ID、内部build-info及OCI来源，Migrator只读无网络CLI，两宿主实际exe与已映射DLL inode均同源b69302b；Agent PID1688516/MaintenanceHost PID1688513，协议仍1.52。16个相关固定helper字节/owner/mode/链接检查匹配；已安装history reader和维护GET专用代理为本次修复，非仅源码HEAD或latest标签变化。

安装后`deploy/host/validate.sh`退出0。11核心healthy，8数据库平台ID/镜像/StartedAt保持；恢复来源列、`20261103085710_ContainerAclRestoreSources`迁移、`ck_container_acl_restore_source`及18个CHECK存在，本次无新schema迁移。可信账本current=b69302b、previous=cad6182、pending=null。7份受保护配置/能力密钥/sudo规则只比SHA/uid/gid/mode，全部保持；user NOPASSWD保留、Agent最小授权不扩大，无主机重启或自动回滚。

发布前只读核验器首次对旧运行容器的Image ID执行image inspect，得到No such image；旧容器本身仍健康且ID/Image/StartedAt不变。修正为仅在发布前读取同一固定容器标签并结合实际CLI；发布后仍严格核对新镜像ID/OCI标签和六组件实际来源。失败发生在触发发布之前，未安装或修改任何服务；失败记录保留。

## 公共8080完整分页：HTTP证据，非Vue截图

在新独占、无网络、只读根、普通UID容器下，使用正常HTTP Authentik登录，访问公共8080的真实BFF→Agent签名历史List。故意选择没有捕获的本轮目标，使扫描遍历整个HDD库存，不能用伪空数据或跳过库存替代。

最终完整门禁退出0：128份元数据分两页扫描，耗时3.597/2.368秒，JSON/no-store、相同库存版本、单调cursor、完整计数和对应审计通过；128份HDD原件字节及九字段前后保持，源完整字节由生产者逐页核对，正常HTTP双端退出与准确容器/源清理通过。新增ACL写入0。原件`deploy/artifacts/container-acl-history-public-e4a85bad6a654d9bacbb8af0ed85ae28/`。

这是真实公共入口及签名读取，不是人类Vue操作，也未人为延迟生产依赖来证明35秒预算。35秒限定GET路由的慢响应边界仍由此前真实loopback测试和实际安装来源证明；本批公共请求均在5秒内完成，证明软分页避免此前完整扫描挤占代理预算。

两次前置探测失败均保留：`container-acl-history-public-b6116df6d4b94b539b673ed676691fec`在首个正常部分页上过早调用只接受完整扫描的旧核验器；`container-acl-history-public-7173118c1b754ac3b793f447d30d400c`已取得81/43两页共124条完整响应，却因旧核验器要求非空捕获路径集而失败。第二批保存响应经新的空历史路径契约和8个反例只读复核通过，没有重发请求，但**原批次仍Failed，未执行正常退出，不补签整批**。修正脚本经离线反例检查后再以全新资源完成上述第三批；两旧源文件/Root历史操作未重放。

## 新资源真实Vue恢复写后撤权

主门禁`validate_container_acl_restore_faults.py b69302b… post-revocation`退出0。两套新独占资源经正常可见Authentik登录→Vue捕获→历史List/Read选中准确旧ACL→新恢复预检/确认→永久PG/Worker→签名Agent→实际ACL/HDD；工具40份来源逐字及SHA冻结，未拦截路由、注入Cookie、改SQL/Root/时钟或使用旧捕获。

| 场景 | 实机结果 |
| --- | --- |
| 写后移出管理员组 | 目标基本access ACL已写、Root仍Applying时只移除本轮临时身份的指定组；产品4.786秒结束原helper，原恢复Succeeded/Completed，新ACL/版本保持。原恢复GET403，身份GET200非管理员，原Vue进入forbidden、私有引用清空，取消POST0。 |
| 写后停用账号 | 同一精确已写切点只停用本轮临时账号；产品4.736秒结束原helper，原恢复Succeeded/Completed，新ACL/版本保持。原恢复GET403、身份GET403，原Vue进入login、私有引用清空，取消POST0。 |

两旧正权限快照仍未到期且大于1小时，撤权来自实时权威身份。两场历史页面均直接成功，`history-unavailable-*`文件0、手动重读0；不以此前重试上限耗尽或单个移组分项替代本批通过。两原上下文属于失权，不冒称正常双端退出。

完整文件字节（两文件各2097159字节）、原inode/属主/mtime与普通属性、非目标ACL、所选旧捕获和两份本次当前ACL前像分别核对；目标ACL/ctime按已写结果核对，不把所有字段笼统说成未变。2旧来源+2新当前HDD、8条专属恢复审计及原Job/执行/来源hash保持；迟到原Apply只回Completed/replayed，原Root字节、版本和现场不重写。4张真实PNG，移组320×600暗色、停用1440×600亮色；手动复看暗色进行中状态/动作区及停用后登录页，无凭据截图。

不同当前管理员HTTP Recovery GET200/no-store、原Owner专属`/restores/{id}` GET404、正常HTTP双端退出均通过；**未冒充另一管理员任务抽屉Vue或本批任务中心页面签收**。

## 独立复核、清理与未验范围

主验原件`deploy/artifacts/container-acl-faults-a10257ef0e5e4fa3973410378e6b1b41/`。独立工具先完成SQL/Root/HDD/审计/准确源容器检查，在最后全局临时账号检查恰与第三批只读HTTP探测的临时登录重叠，首次退出1；原失败日志保留。待探测正常退出清理后，仅重跑独立只读工具，退出0、`independentAclRestorationFaults=true`，未重做任何ACL写入或撤权。清理后的独立工具不重读已删除源字节；物理全字节由主验逐轮证明。

最终11核心healthy，8数据库平台及API/Gateway保持发布后基线，两宿主PID/source、7配置及账本保持；Worker只为精确切点正常停启，全部活动Job/终端/锁/屏障/未完成意图0。两批旧超时失败源树仍保留，不声称长期清理完成。原件、脚本、失败/成功日志与SHA清单保存在root私有忽略目录，不复制凭据进仓库或日志。

本批修复单对象ACL历史读取并签收恢复写后实时移组/停用。恢复写后Agent重启工具profile6/schema12仍仅候选，下一步用新资源实机运行；Unknown/实例或来源变化、磁盘/共享写入、递归ACL/恢复、可恢复删除与长期清理、安装态故障及其余59项仍待。源和HDD同device，不算异盘灾备；59主项及六阶段完整签收数不提升，17项移出不算完成，master未交付。

收尾8项范围检查、874条变动MD相对链接通过，目标第1节逐字保持，3份前批QA原文仅追加接续。图谱确认原whale-deck/container-acl的`whale-deck.container-acl.b69302b.history-timeout-release-real`事件，阶段active，原pending移入synced；没有将两个故障分项记为59主项完整完成。

## 后续接续：恢复写后Agent重启

以上原时点证据保留。随后首次尝试因来源Job证据缺失失败且未登记恢复/重启；96b6bda补齐实际来源Job回执后，使用全新资源完成恢复写后Agent重启、双任务详情/原上下文退出、HDD与独立复核，见[本轮实机和失败记录](CONTAINER_ACL_RESTORE_POST_AGENT_RESTART.md)。当前Agent1772448/维护1688513，产品仍b69302b，Unknown/递归ACL/删除及59项继续。
