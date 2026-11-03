# ACL历史1.52安装态：真实签名List/Read、原件与兼容验收

2026-11-03。**Agent/MaintenanceHost已正常覆盖为`2ef06f0bfb64c63613766d36968bcda521c5ff05`、协议1.52，真实签名UDS历史读取及独立核验通过。** 这是只读历史能力的安装态分项，不是历史恢复执行、永久历史目录/API/Worker/Vue或Authentik页面验收，59项主任务与阶段不提升整项完成率。

## 冻结来源、安装与运行边界

产品协议/SDK/helper来自[6013aa1候选](CONTAINER_ACL_HISTORY_RPC.md)；新增原生探针及主/独立门禁冻结在2ef06f0，构建、宿主安装与成功RPC探针均为2ef06f0，不用后续文档提交冒充运行来源。固定unit控制验收工具另记`b11d8016b3be2fd626fb0b17bbaf166cc337d2fa`，没有因此重装宿主。

标准`publish.sh`及`bash publish-terminal-probe.sh`从Git归档、锁定digest的SDK构建；私有env不挂入构建容器。宿主制品`host-publish.1K7Cyt`与探针`terminal-probe.Dusewp`的SHA清单、两宿主build-info及自包含NETCore/AspNet/PKCS 10.0.13通过。首次直接执行无执行位的probe脚本返回126，未开始探针构建/系统操作；原日志保留，改用bash成功，没有更改脚本执行权限或绕过标准构建。

仅一次宿主安装unit `whaledeck-acl-history-install-2ef06f0.service`，Invocation `0552122d971a42f8a925b5455217fa89`，Result=success/ExecMainStatus=0；原始journal保留。完成后systemd已卸载本次瞬态属性，Invocation由原journal核对，不将空InvocationID解释为未执行或再发一次安装。标准install/validate通过两服务、0700状态/0750新模块、真实初始namespace/准确FD非IDMAP依赖、受限sudo/UDS及动态网卡维护入口。

Agent PID315250→561098、维护315245→561094；实际/proc exe build-info与运行映射DLL inode/SHA一致，NRestarts均0。**11核心容器ID、镜像、StartedAt与安装前相同且healthy，API/Worker/Gateway及运行checkout仍0478262**；没有发布应用容器或迁移。全栈可信发布账本仍currentRevision=0478262、previousRevision=134fd25、pending=null，不把本次仅宿主安装登记成六组件发布完成。

capability key、HDD配置、文件根、Agent sudo与开发sudo的SHA/owner/mode保持；visudo与user sudo -n true通过。产品Agent未获任意Python/shell或新的sudo授权，user NOPASSWD按授权保留。维护/status实际healthy/Ready；未对安装窗口声称零瞬断，未自动回滚或重启工作站。

## 真实签名RPC与物理原件

新独占证据目录`deploy/artifacts/container-acl-agent-12218c146bbd48d88cf8971542a92f85/`。无网络、只读容器根、非root1000:1000、唯一准确HDD bind、固定资源预算；没有复用前批已签收操作。先经原ACL签名协议完成9操作（8 Completed/1执行前Aborted）、8份真实HDD原ACL，逐轮完整2097159字节/原inode-owner-mtime/两类ACL/普通属性/非目标子项、Root与迟到签名重放均核对；旧ACL的13预检/4执行拒绝同时通过。

| 新历史签名UDS检查 | 实际结果 |
| --- | --- |
| List按当前CID/实例/准确路径、take=1分页 | 10页，原文件空历史与两个ACL目标捕获集合准确、无重复，cursor前进/固定version/最终complete |
| Read所选捕获与预期historyDigest | 8份完整原access/default ACL，与每份原plan和真实HDD逐字段一致；显式named UID0/permissions0及空default保留 |
| List边界拒绝 | 19次：缺MAC、ACL写域/文件历史域、过期/远期、错方法、完整体字段/Subject/Trace/分页篡改、非法take/cursor/context/job、核心资源、旧库存版本及nonce重放 |
| Read边界拒绝 | 10次：缺MAC/错域/错方法、捕获/摘要篡改、实际旧摘要、缺捕获、其他路径、当前目标缺失及nonce重放 |
| 每次成功响应后的源/Root/HDD保护 | 全源事实与9份Root SHA不变，8份HDD的完整原字节SHA及9字段身份在前后和独立重读中一致 |

有界输出/optional presence由原生探针实际检查，再由独立Python逐捕获核对；不以C# JsonFormatter的省略字段冒充0。wrong digest实际Aborted，缺捕获/其他路径/目标缺失实际NotFound，核心实际PermissionDenied，没有将缺目标报告为空历史。非目标路径只用于明确拒绝或空历史证明，不暴露其历史正文。

真实读取期间采到新unit的Invocation/PID与实际配置：仅`cap_dac_read_search cap_sys_ptrace`，NoNewPrivileges=yes、ProtectSystem=strict，唯一ReadWritePaths=`/var/lib/whaledeck-container-files`；没有ACL state/HDD/应用目录写授权。真实当前ContainerRoot、namespace与FD非IDMAP证明均由已安装helper执行，不使用映射夹具。

主门禁Passed后独立进程复核原始签名RPC转录、18份查询响应、29拒绝计数、原Root、8份attested HDD摘要/原字节身份及准确清理，独立Passed。只在全源证明后逐文件unlink/空目录rmdir，再由固定fixture移除准确标记容器；Root/HDD历史保留。独立检查不声称重读已经清理的源字节。源与HDD都为device2049，**不计NVMe到异盘备份/异盘灾备**。

## 固定控制与旧能力兼容

控制目录`container-acl-history-control-43485dab36ec4128a9e21d2527dd6c4b`：实际`whaledeck-agent`身份经原sudo白名单启动两次只读helper，未发送目标请求。错Invocation停止返回2且原unit/PID不变，正确Invocation才停止；第二次stdin EOF返回固定错误并退出，两次unit准确消失、核心/两宿主不变。**这不是查询进行中的gRPC取消、超时或Agent重启验收**，不能由控制前置分项补签这些故障组合。

| 1.52上旧签名能力回归 | 新独占目录（deploy/artifacts下） | 主门禁与独立复核 |
| --- | --- | --- |
| 单对象所有者 | `container-ownership-agent-8148885cb4bb4c858717aceba48688d4` | 6操作/5完成/1中止，5份HDD，均通过 |
| 递归普通权限 | `container-recursive-permission-agent-5fdfe4dbc52d471ba8aeb41c578a84ca` | 4操作/3完成/1中止，3聚合+12子项HDD，均通过 |
| 递归所有者 | `container-recursive-ownership-agent-1091e7bda6864563b8fb9638e5ea5a63` | 4操作/3完成/1中止，3聚合+12子项HDD，均通过 |

三组均准确清理各自新资源，全字节与适用inode/模式/所有者/mtime/普通属性按原门禁验证。它们是协议兼容验收，没有人类BFF登录、永久业务任务或页面。

## 自动检查、原件与未验范围

本轮探针Release构建零警告错误，58历史RPC C#专项与8项旧/新探针proof校验通过，相关Python编译、bash语法及diff通过。前批4343全量Unit/432 Integration（7默认原生跳过）、28 Linux-root历史/传输和431旧文件回归保持前批来源，不冒充本轮全部重跑；无前端/浏览器测试或新远端CI全绿结论。

安装/构建/失败调用、原journal、安装前后私密配置仅SHA、实际映射来源、最终健康与冻结源码保存在`deploy/artifacts/acl-history-native-install-20261103/`；上述实际RPC、控制和兼容原件均root/0700、文件0600，SHA清单留存。实际capability与凭据不进入Git或输出；probe只在内存读固定key并清零。

尚未验：真正查询进行中取消/重启/超时/HDD失联、更多实例/路径/挂载变化及共享库存干扰。库存version/invalid统计仍全局，不签多租户统计隔离。尚未实现历史管理员目录/API/Vue、正常Authentik登录/授权/退出整链、选择历史后的新计划/确认/先留当前ACL原件/永久恢复意图/Worker/故障对账；更非完整递归ACL或历史恢复。继续这些能力、可恢复删除/长期清理、维护安装态故障及其余59项/UI/C#酒馆/统一供应商/AI Live2D；17项移出不算完成，master尚未交付。

记录收尾：8项范围与8项探针proof通过，7份变动MD的804条相对链接无缺失，目标第1节和6013候选原正文逐字保留。6个私有证据目录共142份静态文件SHA校验通过；收集器产生的4份0644快照原先已在0700父目录内，收尾统一为0600并保留模式修正记录，没有改动原件内容。
