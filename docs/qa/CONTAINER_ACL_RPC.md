# 容器ACL：独立签名RPC/SDK与固定传输候选

后续更新（2026-11-03）：真实入口初始化缺陷已修复并正常覆盖到4a58961/1.51，工具224f744单独记来源的真实四RPC/HDD/拒绝/旧能力兼容及独立核验已通过，见[安装态证据和两次失败](CONTAINER_ACL_AGENT_RPC.md)。以下6a62027候选/未安装正文逐字保留，未将其测试夹具改称原生证据；永久业务/真人页面仍待。

2026-11-03。候选冻结`6a620275a9f8c56a92275d13b78e63c0ebd9462e`，编译Agent实际`--build-info`来源一致，协议1.51。**未安装、未执行真实签名UDS RPC或人类业务页面**；运行Agent/MaintenanceHost仍b1fafed/1.50，API/Worker/Gateway及11核心保持。此前[ed50dc1底层真实容器/HDD候选](CONTAINER_ACL_ENGINE.md)不重做，不把其9操作/2强杀冒称新RPC证据。

## 独立协议、完整绑定与SDK

新增`ContainerAclService`的Plan/Apply/Inspect/Discard四个unary方法，独立命令/计划/回执、`ContainerAclBinding` request/hash/capability域。protobuf显式保存ID和permissions的presence：named ID=0和权限0均有效，未提供不能默认为0。command明确access/default与规范有序条目；plan绑定原access/default ACL、原inode九字段/父身份/实例/普通属性、目标模式和noChange。

Agent与Application分别严格校验64项预算、数值ID范围、基础项唯一、named项必有mask、权限0–7/规范顺序/目录default限定；targetMode由access mask或原模式推导，不能伪造模式/noChange或夹带递归/属主操作。回执逐项绑定原operation/container/instance/plan/proof/kind/ACL type/目标模式；Completed需显式aclConfirmed及已核历史/版本，Aborted确认false，Unknown不得确认或携带当前成功版本。原权限和所有者协议未扩大ACL/xattr能力。

Agent全局旧MAC校验拒绝ACL服务，由独立Interceptor分派精确四方法，HMAC同时绑定方法、完整protobuf descriptor+请求字节摘要、45秒期限和单次nonce。服务还重算请求摘要与header固定时间核对；修改目标/条目ID/权限/mask/presence、原ACL、目标模式、原身份/计划、actor/trace/job/key都会失效，跨服务/过期/超长期/旧MAC/重放拒绝。

SDK `IContainerAclGateway`/`AgentGateway.ContainerAcl`要求major1、minor≥51及专属`docker.files.acl-intents`，旧1.50或缺能力不会发送计划；只有准确私有NOT_FOUND映射空回执，目标失联/历史错误不能当“未发生”。ACL command和entries使用结构比较，避免IReadOnlyList引用相等误判。只有明确预检拒绝映射有界业务错误，执行结果仍须原Root观察。

本批无永久ACL意图/EF迁移/共享保护/Worker/API/Vue；可信SDK不是新的用户授权入口。Authentik/BFF/当前身份/原登录及二次确认与故障矩阵必须在后续业务接入完成，默认User未获授权。

## 固定传输、最小宿主权限与身份映射

新增固定`container-acl:{plan,apply,inspect,discard}` runner和`container-file-acl[-stop]` helper入口；私有stdin、有界JSON/EOF、准确unit及Invocation停止，拒绝其他动作/额外宿主路径/命令注入。root模块allowlist、独立`/var/lib/whaledeck-container-acl`、root:root/0700状态和0750模块，安装/验证与CI编译清单已接。

独立unit仅保留DAC_OVERRIDE/DAC_READ_SEARCH/FOWNER/SYS_PTRACE，**没有CAP_CHOWN或CAP_SYS_ADMIN**；固定apps/HDD/ACL状态写域、AF_UNIX、70秒/192M/8任务/CPU50%和64KiB文件预算，ProtectSystem/PrivateDevices/NoNewPrivileges/禁止namespace创建及Agent BindsTo保持。旧chmod/chown/递归unit约束不变；开发user NOPASSWD不是产品授权。

数字ID只在已证明的初始user namespace和非idmapped准确FD mount中接受，不猜测用户名或转换容器数字：每次parent确认原PID/session稳定且其user namespace与helper/PID1一致；snapshot和parent通过Linux `statmount(2)`的BY_FD/MNT_BASIC查询，拒绝MOUNT_ATTR_IDMAP。缺syscall/不支持架构（当前仅x86_64/aarch64）/错误/缺mask/不完整结果均MAPPING_UNPROVEN，不按filesystem名字或路径猜测。依据[Linux mount UAPI](https://github.com/torvalds/linux/blob/master/include/uapi/linux/mount.h)核对32字节request、512字节输出上限及属性字段；不增加挂载/namespace写权限。其他映射形态目前明确拒绝，不算支持映射转换。

`validate.sh`新增只读初始namespace、准确apps FD mount属性和实际POSIX ACL xattr依赖检查。ENODATA仅无ACL，ENOTSUP明确UNSUPPORTED；EACCES/EIO/无关ENOENT不冒充空ACL/未发生。部署前须实际支持这些能力，候选静态安装测试不是已安装验证。映射证明在写入后丢失时恢复Unknown，不重写；目标消失则按原回执只观察，不用普通Docker/HDD故障创建伪墓碑。

## 验证输出与失败记录

| 检查 | 实际输出 |
| --- | --- |
| 冻结6a62027全方案Release | Build succeeded，0 Warning/0 Error；Agent build-info sourceRevision=6a620275a9f8c56a92275d13b78e63c0ebd9462e |
| 后端Unit | 4132 Passed/0 Failed/0 Skipped，包含96项新增ACL策略/绑定/SDK专项 |
| Integration | 363 Passed/0 Failed，7项默认原生依赖测试Skipped，未冒称真实PG/Valkey新验收 |
| Linux容器文件 | 468项全部通过/0跳过；其中ACL共34项，12项为本批传输/mapping/profile新增 |
| 安装/范围 | 12 host runtime、5安装CLI无系统操作、8范围；Python -W error、bash语法、diff通过 |

SDK使用明确HTTP/gRPC帧夹具，身份/Root为测试输入；Linux测试真实临时inode ACL，但ContainerRoot和mount返回可明确合成。测试覆盖statmount缺结果/越界/IDMAP/namespace、四动作/迟到栅栏/准确target失联、错误脱敏、映射写后丢失及最小profile；不是安装态UDS/实际受限unit或真人页面。没有本轮前端测试/页面变更，不借旧浏览器或旧实盘结果补签。

首轮新C#测试编译有两个错误（RepeatedField.Add的new()重载歧义、AgentGrpcService测试构造缺Docker参数），已修复并保留`wd-acl-rpc-unit-first.log`，后续94专项、补UNSUPPORTED/MAPPING SDK拒绝后全套4132通过；不是产品发布失败。附加只读汇总器首次sudo PATH找不到dotnet，保存`readonly-preflight-first-failure.json`；改用已核对绝对路径仅重读汇总后通过，不重复ACL或旧实盘操作。

## 当前工作站只读证据及剩余

私有目录`deploy/artifacts/container-acl-rpc-candidate-6e27061c782f42419ede25c318e8dd01/`。33个改动文件逐字与冻结Git blob/SHA核对；保存编译身份及全套测试/首轮错误日志。2026-11-03 00:27:07+08:00只读确认初始user namespace、实际apps FD mount属性0x10（无IDMAP）和POSIX ACL读取支持；此前普通用户临时FD statmount BY_FD成功，无CAP_SYS_ADMIN。这里只证明这些实际读取，不签容器内受限unit的RPC执行。

当前ACL安装文件与状态目录均不存在，签名RPC执行次数0。11核心ID/镜像/状态/StartedAt与ed50dc1批次相同且healthy；实际Agent PID2542378/MaintenanceHost PID2380322及b1fafed来源保持，任务/终端/主机锁/八类意图/屏障/租约0。源码候选与已装版本分开，未发布/回滚或修改生产schema/用户密码/sudo状态。

原件root/0700、文件0600，不入Git；`readonly-preflight.json`记录原生只读证明、33 SHA、实际未安装与测试摘要，原始日志`/tmp/wd-acl-rpc-{unit-first,unit-full,integration-full,linux-full,release-frozen}.log`保留，无凭据。

下一步准备原生签名RPC探针、正常冻结宿主覆盖，实际验证四方法/access/default/显式0/拒绝/重放/HDD/准确清理及旧1.50能力兼容；再永久SQL/共享保护/Worker/API/Vue和真实Authentik页面/失败恢复。ACL递归/历史恢复、删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续，17项移出不算完成，master尚未交付。

文档检查：221份Markdown/1339处相对链接零缺失，范围8项与diff通过；原ACL底层QA正文逐字保留（SHA256 `92fe11ce82d84b61eaa08705b1dafcfa88fd408fd3a61c34003d46545f49fd30`），仅前置后续说明，不改旧原件/失败/版本事实。
