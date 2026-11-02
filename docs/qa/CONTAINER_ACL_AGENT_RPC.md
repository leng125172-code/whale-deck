# 容器ACL 1.51安装态：真实签名四RPC、HDD及旧能力兼容

2026-11-03。宿主正常覆盖后，实际Agent/MaintenanceHost运行来源为`4a58961d638b3e6f89073e59fb893ab7b22552e8`，协议1.51；成功ACL验收工具来源另记`224f744b535c8950addb1e6c46674a7e2a59f16d`，不把工具新提交冒充运行版本。API/Worker/Gateway仍b1fafed，11核心容器未重建。**真实签名UDS/固定受限helper及实际HDD已通过；永久ACL业务/共享保护/Worker/API/Vue与真人页面未实现，不计完整ACL或59项完成。**

## 冻结发布、安装和实际运行来源

标准`publish.sh`与`publish-terminal-probe.sh`使用锁定digest的SDK，从Git归档构建；源码包无`.env`/`.images.env`，不挂工作区或私密配置。root私有制品/SHA清单、两宿主实际`--build-info`、10.0.13 NETCore/AspNet/PKCS依赖核对通过。安装前重新确认全局Job/终端/锁/八类意图/屏障/租约0、HDD实际attest、核心快照和旧宿主进程来源。

| 实际批次 | 来源、过程与结果 |
| --- | --- |
| 首次标准覆盖 | aa1a84f；host构建83.185秒/probe60.073秒，安装及validate6.323秒，Invocation `3ee07702368b4036aa56a27be6fc22aa`；安装检查通过，但首轮真实RPC发现初始化缺陷，不能签ACL可用 |
| 修复正常覆盖 | 4a58961；host548.855秒/probe655.680秒，安装及validate6.138秒，Invocation `f36565d32cca4af789926ed5a106b609`；SHA/运行来源/实际依赖通过 |
| 仅修验收工具 | 224f744；probe92.062秒，来源/SHA核对后直接测试已正确运行的4a58961，不重复安装宿主 |

4a58961构建曾遇Grpc.Tools.2.84.0公开NuGet下载EOF，标准流程重试后成功。只读核对过本机公开包的NuGet仓库签名、规范内容hash与assets一致及原包SHA；缓存复制前的日志文案断言失败、未创建缓存stage，**没有向构建容器复制缓存/凭据、改变依赖版本或关闭审计**，`public-cache-not-used.json`与公开签名日志保留。

运行中`/proc/PID/exe --build-info`实际核对：Agent PID2542378/b1fafed→2777253/aa1a84f→2818452/4a58961；MaintenanceHost PID2380322→2777247→2818448，对应同源。发布是两次正常覆盖，无自动回滚。user NOPASSWD文件SHA/owner/0440不变，visudo及`sudo -u user sudo -n true`通过，密码未改；产品Agent仍仅固定helper。

构建与安装unit各自保存准确InvocationID/Result/MainPID/ControlPID；仅成功且PID0的本次unit停止收尾，无宽泛reset-failed或清制品。标准validate同时通过ACL模块/0700状态、实际初始namespace/非IDMAP FD依赖、动态入口及维护入口检查；正常安装重启维护进程，不宣称整个覆盖窗口无任何瞬断。

## 两次失败及修复，原件均保留

1. `container-acl-agent-c6199ba57c2d44f2ab38b34ecc59119e`：helper入口仍引用旧`ContainerOwnershipHistory`，首次进入真实helper即Unavailable；未生成ACL Root或该CID的HDD历史，完成操作0。4a58961修为`acl.acl_history_type(permission)(metadata)`，抽出实际main共用`create_engine`，新增真实临时inode/历史构造回归，避免只测已注入engine而漏初始化。原Failed不改为Passed。
2. `container-acl-agent-5d2f42692a294723a8bfc6a1fb1cca64`：13项真实预检拒绝已通过；执行负例把noChange改成非法计划，服务正确InvalidArgument，探针误期待PermissionDenied，仍未写ACL/Root/HDD，完成操作0。改为C#策略仍合法的sourceChangedAt变更，新增“先通过语义校验再核对哈希变更”回归；工具/运行来源分开记录。服务授权不放宽，宿主不为此再次覆盖。

两次均保留原rpc.stderr/jsonl/failure。随后完整实读原ACL对象、字节和九字段、Root空与该CID无HDD记录，才准确清理本轮随机UUID资源。首批框架original.bin漏保存初始inode，恢复只证明其专属UUID衍生完整内容/当前属性与根拥有目录，不宣称该文件初始inode连续；该限制写入`failed-source-recovered.json`。后续baseline已补框架原件指纹/SHA，第二批所有对象与初始指纹一致。无外部用户/生产数据删除，也不重放失败批次。

## 成功的真实四RPC与逐轮实盘证明

成功目录：`deploy/artifacts/container-acl-agent-369a39864f7647c380f358cb622b6c23/`，运行4a58961，探针224f744。新独占容器无网络、只读容器根、执行1000:1000、唯一准确HDD bind及资源预算；两文件各完整2097159字节、2100:3100、普通mode/纳秒mtime/user属性，另有保留的框架原件。所有path只限本次两个固定ACL对象，不接受任意宿主路径。

| 实际RPC场景 | 结果 |
| --- | --- |
| 文件access named user、no-op、mask变更、显式named ID=0、缩减基础ACL | 5 Completed，显式0不等于缺失，无属主变更 |
| 目录default添加、default no-op、清除default | 3 Completed；access/mode及既有子项不改 |
| 执行前Discard及迟到Apply | Aborted，原件不改，无HDD历史/成功版本 |

共**9操作、8完成/1中止、8份HDD原ACL元数据**。每轮Plan前后读完整文件、两ACL和九字段，确认只读；每轮执行后先核原实例/执行身份/原属主/清单、Root/plan摘要、HDD history域、实际版本和目标ACL，再逐次Inspect/Apply/Discard重放，逐次比对Root原字节、全源事实不变。两种no-op九字段含ctime完全不变；仅允许目标ACL及显式mask导致的mode/必要ctime变化，所有者/inode/内容/mtime/user属性和非目标ACL/子项保持。

13项实际预检拒绝覆盖无MAC、错域、过期/远期、错方法、请求内容变更、缺ID/permissions/mask、重复项、core、只读根及nonce重放；4项执行负例覆盖合法语义的完整请求篡改、错方法、错域、原Job不匹配。真实运行的固定unit无CAP_CHOWN/SYS_ADMIN；namespace/准确FD非IDMAP检查实际通过，不以mock映射代替本轮RPC。

结束后再经同一签名UDS发送首个旧操作Apply，仍原Completed/replayed但bindingCurrent=false，Root/全源不重写；此处是**真实签名RPC**，区别于此前其他批次的固定privileged helper迟到探针。HDD保存原ACL/身份而非文件内容，处置/重放不能伪造当前成功。

主门禁Passed后独立进程重读实际Root、attest HDD、版本/摘要、原请求/逐轮事实与准确清理，独立Passed。只在完整证明后逐文件unlink/逐空目录rmdir；实际CID/源目录消失，Root/HDD保留。独立观察不声称清理后重读已删除字节。源/HDD均device2049，只计同设备历史，不计异盘灾备或NVMe业务覆盖。没有本批人类Authentik登录、BFF、永久意图或Vue截图。

## 同一1.51宿主上的旧能力兼容

| 回归 | 私有证据目录（deploy/artifacts下） | 结果 |
| --- | --- | --- |
| 单对象所有者 | `container-ownership-agent-d358ff1057bc431f93985fe592dbb81b` | 6操作、5 HDD；主/独立Passed |
| 递归普通权限 | `container-recursive-permission-agent-84d9ac164a5048eea8a075540fb0ae80` | 4操作（3完成/1中止）、3聚合+12子项HDD；主/独立Passed |
| 递归所有者 | `container-recursive-ownership-agent-9c92d78ad3604a6ca1d226b6d99eafed` | 4操作（3完成/1中止）、3聚合+12子项HDD；主/独立Passed |

旧递归所有者门禁首次因主工作区HEAD已前进到工具224而运行版4a仍旧，在无资源创建的只读预检拒绝。保持原严格门禁，在4a58961冻结工作树和4a编译探针执行；独立Passed后26份证据按SHA完整转存主私有目录并删除临时工作树。复制汇总首次误读仅stdout存在的成功marker，复制未开始且root私有原件完整；修正按文件内independentReadOnlyCheck核对后26份字节全部保留，原错误/路径回执在`compat-worktree-evidence-preserved.json`记录。未修改旧运行/验收正文或降低源码检查。

## 最终状态、自动检查与下一步

2026-11-03 01:24:49+08:00最终只读：11核心ID/镜像/运行状态/StartedAt自首次安装前不变且healthy；Agent PID2818452/MaintenanceHost PID2818448/实际4a58961稳定。维护状态实际HTTP200/healthy/Ready/no-store；全局任务/终端/锁、八类业务意图/屏障/租约及四类本批transient均0。API/Worker/Gateway未覆盖，仍b1fafed。

469项容器文件Linux-root全套零跳过、97项ACL C#专项、3项新探针边界、12 host runtime、5安装CLI无系统操作、8范围及Python/bash语法/diff通过；探针构建零警告错误，自包含运行库10.0.13验证通过。旧4132后端/363集成和7默认原生跳过保持上批记录，不冒称本轮全量重跑；本批没有前端/浏览器测试。

安装/构建原件位于`acl-host-aa1a84f`、`acl-host-4a58961`、`acl-probe-224f744`；成功/失败/三回归证据目录root/0700、JSON与日志0600；可执行制品按标准执行位/运行库模式并核对SHA，凭据只在内存或固定私有文件，不进Git/日志。所有门禁、失败与准确恢复原件保留，最终健康快照复制至成功目录；构建/探针输出路径和SHA各自记录，不以新探针冒充新宿主。日志`/tmp/wd-acl-installed-{first,fixed,final,independent}.log`、`/tmp/wd-acl-compat-*.log`保留。

下一步实现永久ACL意图/迁移、共享写入保护/租约/Worker、实时人类授权与原登录绑定、API/Vue，再真实Authentik→页面→永久任务→Agent→对象/HDD常规和故障矩阵；ACL递归/历史恢复、删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续。17项移出不算完成，master尚未交付。

文档收尾：222份Markdown/1346处相对链接零缺失、范围8项与diff通过；原6a62027 RPC候选正文逐字保留（SHA256 `e7410a7634ea2582e93d45c07b560f9612754b6727d9685a837253861742b649`），仅前置后续说明。
