# ACL历史超时修复：冻结构建与发布前基线

2026-11-03。本批接续[根因、修复候选和失败记录](CONTAINER_ACL_HISTORY_TIMEOUT.md)，产品候选固定为`b69302b8cb32048038d7dbeed193af205c70fd11`。**四镜像预构建已完成，标准构建退出码0；API、Worker、Migrator、Gateway均已核验内部版本、实际镜像ID和OCI revision为b69302b。尚未覆盖发布或重做真实撤权操作。**

## 实际推进与已核验结果

运行源码目录由其所有者从干净cad6182快进到b69302b，Git对象从开发仓库本地获取，保留外部私有配置。运行源码HEAD、候选镜像和当前运行版本分开记录；没有把latest标签当作运行版本证明。开发用户索引仍user:user/0664。

仅运行标准`deploy/build-images.sh`：冻结Git archive上下文，私有`.env`、`.images.env`和`.git`不进入构建上下文，五类基础镜像仍要求不可变digest。API、Worker、Migrator、Gateway的实际镜像ID、OCI revision和镜像内部CLI/build-info均核验为b69302b；检查采用无网络、只读、cap-drop、no-new-privileges的临时容器，检查后自动移除。Migrator系统源下载33.7MB耗时7分24秒，随后完整安装`libgssapi-krb5-2`及实际依赖并成功产出第四镜像；没有删依赖或忽略下载结果。初次三镜像观察快照保留，与最终四镜像成功回执分列。

最初观察回执ready为api/worker/gateway、pending为migrator；最终四镜像核验回执ready包含全部四个、pending为空，published=false。11核心healthy，核心ID/镜像/StartedAt、两宿主PID及来源均与本批部署前基线相同。Agent1399935、MaintenanceHost1214273仍cad6182/1.52，公共8080状态接口200/source cad6182，已安装history helper字节仍为cad6182。

7份受保护文件SHA/uid/gid/mode保持（只保存摘要，不复制凭据），包括user NOPASSWD规则与Agent最小授权配置。发布账本仍current cad6182 / previous 08b2bb8 / pending null。全部活动Job、终端、锁、屏障及未完成文件意图预检0。未启动发布unit、宿主安装、生产迁移或运行容器替换；未创建新的真实恢复意图或Root/HDD写入，也未重放两批失败资源的操作。

私有忽略原件目录`deploy/artifacts/acl-history-release-b69302b/`保存本批before.json、prebuild-observed.json、最终prebuilt-verified.json、两份日志及只读核验脚本。会话13610已退出0；完整构建日志末尾为准确b69302b成功标记。最初观察快照不改写为成功，另存最终四镜像回执；后续无需重复启动预构建，标准发布仍会按正式流程重新验证冻结构建。

## CI与下一步门禁

准确候选[run41647839607](https://github.com/leng125172-code/whale-deck/actions/runs/41647839607)的backend、installer、native-postgres、native-valkey已completed/success。frontend已通过容器布局组，进入最后的生产授权/文件传输/主机与容器终端页面组，仍in_progress；未把活动步骤记为全部用例通过。GitHub插件一次传输失败后改用已授权gh读取同一run/job，未改TLS或更换仓库/账号。

待准确候选CI五路成功终态，再用固定维护update标准覆盖。发布后重新核对实际六组件、安装helper、迁移状态、核心健康和可信账本；不能用本批四镜像预构建或源码快进替代部署。

随后用新独占资源经公共8080、正常Authentik登录和Vue历史列表/快照选择，完成恢复写后移组与账号停用两场景及独立只读复核。内部18080对照仍只作根因证据；此前一个移组分项不升级为整批通过，两个失败源树和失败回执保留。历史分页、超时修复及真实撤权整链尚未签收。

59项整项完成率及25%–35%粗估不变，17项移出不算完成。其余写后Agent重启、Unknown/实例或来源变化、磁盘/共享写入、递归ACL/恢复、删除和长期清理、其余功能及可部署master交付仍待；本批不自动回滚dev或重启主机。

## 文档检查与同步状态

8项范围测试通过，4份本批相关MD的607条相对链接有效；目标第1节逐字保持，前批超时QA原始正文只追加接续。项目图谱接口本次传输失败，稳定事件`whale-deck.container-acl.b69302b.prebuild-started`保存在本地pending待同步；未声称网站更新成功。

## 后续同步确认

76cb825已重新推送至dev，远端精确SHA确认成功。项目图谱以原`whale-deck.container-acl.b69302b.prebuild-started`重试，回执项目/阶段/事件一致，原pending已移入synced；以上原时点网络失败和三镜像观察均保留。仍未部署或取得新增实机签收。
