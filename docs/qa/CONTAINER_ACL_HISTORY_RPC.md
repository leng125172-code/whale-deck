# ACL历史协议1.52：6013aa1签名RPC/SDK与只读传输候选

2026-11-03。**候选6013aa163193f4dfe0334e75b28a9141f331b084完成独立List/Read协议、完整请求签名、SDK及受限只读helper，测试通过并推送；尚未安装或发布。** 实际仍0478262/协议1.51、Agent315250/维护315245，11核心healthy，新历史helper两文件在生产均不存在。旧818396e真实HDD读取分项保持[原来源与范围](CONTAINER_ACL_HISTORY_READER.md)，不能改标为本批实际签名RPC或页面通过。

## 独立协议与权限边界

新增`ContainerAclHistoryService`两项unary方法List/Read及typed summary/page/snapshot，请求都包含当前容器/实例/规范非空路径与Subject/Trace；List另外绑定after/take/expectedVersion，Read绑定captureId/expectedHistoryDigest。没有Apply/Inspect/Discard/恢复/任意宿主路径或内容流，原ACL写协议不复用或扩权。

能力域、请求哈希域、方法全名和protobuf完整序列化独立于普通ACL、内容历史及通用capability；包括Subject/Trace和每个分页/捕获字段。使用现有45秒签发、最多60秒验证、随机nonce一次使用及固定时间比较；通用旧token不能进入新服务，跨方法/域/改体/过期/重放拒绝。Context.Capability必须为空，读取不借用旧执行Job/幂等ID。Actor字段是可信调用方的绑定，不等同已经做过人类授权；后续业务/API仍需当前Authentik管理员及资源授权。

Agent候选协议升到1.52，单独广告`docker.files.acl-history`。SDK必须major1、minor≥52且具此能力，没有就返回Unimplemented，不降级到旧ACL写或文件内容历史。新增`IContainerAclHistoryGateway`、Application DTO/校验和DI，暂无历史API Controller、持久SQL目录/Worker或Vue页面，也没有额外数据库迁移/依赖包。

## 输出证明与显式零值

Agent逐字段验证helper JSON，拒绝重复/额外/缺失字段、错CID/实例/路径、旧库存版本、乱序/重复捕获、超预算或不前进cursor；只投影安全字段，不返回原owner/完整plan/inode身份/属主及宿主路径。单页≤50、scanned≤200，global invalid计数不能超过scanned，摘要及snapshot protobuf体积分别≤60KiB/24KiB。Root JSON行仍限64KiB，摘要与两类ACL均有现有64项边界。

合法0/false不靠省略推断：原模式、创建时间、access/default条目计数、scanned/invalid计数及complete均为显式optional scalar，Agent和SDK验证Has位。ACL条目的用户/组ID和permissions仍保留现有存在位，named UID0、权限0、模式0000、空default ACL、epoch0与complete=false在有明确证明时合法；省略不能冒充零值或“未完成”。

Read必须命中所选捕获和预期摘要；两类ACL结构、数量、mask有效模式与原模式一致。SDK自己复核返回位置/版本/cursor/捕获/ACL和大小，并映射全新的数组，不将protobuf或raw plan透传给上层。页内库存统计仍是全局，条目正文才按CID/路径过滤；本批未改变前批已记录的统计侧信道/跨CID分页干扰边界，不把它签为多租户库存隔离。

## 固定只读宿主传输

新增固定stdin helper `whaledeck-container-acl-history-transport.py`，仅接受list/read和固定模块；通过现有ACL bridge构造真实校验器/存储但不暴露其写方法。每次固定当前ContainerRoot，检查初始user namespace，并对当前目标准确FD前后用statmount BY_FD证明非IDMAP；当前目标必须是现存普通文件或目录，目标被换/消失、映射不明或实例变动拒绝。此传输比内部reader更严格：内部历史快照存在不代表可越过当前目标缺失的门禁。

当前目标消失明确TARGET_MISSING，已证明存储中捕获缺失明确NOT_FOUND；HDD基目录/配置丢失、I/O或不能证明存储可用返回UNAVAILABLE，不伪装空历史。错误只输出固定code；不回显原始异常、用户输入、stderr或HDD路径。私有stdin首帧/EOF、driver InvocationID、有限时和无尾随输出由共享runner复用。

新增独立wrapper动作/stop，动作及ID严格白名单；systemd unit固定前缀及真实InvocationID匹配后才停止，不接受任意unit。ProtectSystem=strict、NoNewPrivileges、AF_UNIX、内存/CPU/FD限制、RuntimeMaxSec35，唯一ReadWritePaths是既有容器文件读租约目录；它是两个kernel flock读槽的控制文件，**不是ACL state/HDD/应用数据写权限**。CapabilityBoundingSet仅CAP_DAC_READ_SEARCH、CAP_SYS_PTRACE，不含DAC_OVERRIDE/FOWNER/CHOWN/FSETID/SYS_ADMIN。openat2沿用既有固定非执行driver的RestrictSUIDSGID例外，不对其他服务放宽。

host install/validate已在源码登记新reader/transport，要求root:root0750、单链接/非符号链接；正式Agent sudo规则仍为原固定wrapper，未加任意Python或宿主命令授权。**这些安装源码本批没有在生产运行**，最小cap组合/实际FD映射、取消终止及真实HDD查询须安装后验证，不能由静态profile测试补签。

## 本轮执行的检查

| 检查 | 最终结果 | 证据界限 |
|---|---|---|
| Release全解决方案 | 0警告/0错误；冻结后再构建且Agent build-info=6013aa1完整SHA | 本地候选程序集，不是生产来源 |
| .NET Unit全量 | 4343通过/0跳过 | 冻结后重新运行；包含下方58专项，不重复相加 |
| .NET Integration | 432通过/7默认原生跳过 | 身份/存储/Agent等有测试夹具，未新跑实际PG/Authentik |
| 新C#专项 | 58通过/0跳过 | 两方法、独立域/全体字段/时效nonce、版本门禁、框架路由、精确输出、显式0/false、省略字段拒绝；SDK采用内存HTTP2/gRPC帧夹具，不是实际UDS Agent |
| 历史与新传输Linux-root | 28通过/0跳过 | 15已有reader+13传输；实际临时ACL/FD与元数据，但ContainerRoot/IDMAP/mount提供者为显式夹具，不是新生产helper |
| 既有容器文件Linux-root | 431通过/0跳过 | 旧能力回归，未重新执行旧已签真实写入 |
| 维护/发布离线回归 | 57通过 | 未做新发布或回退 |
| 安装CLI预览 | 5通过 | 明确无系统操作 |
| 三shell/Python语法及diff | 通过，CI installer接新源码/专项 | 不宣称新远端CI全绿 |

只读生产核对：11核心健康、Agent/维护PID与实际运行exe inode及CLI来源仍0478262，两个新history文件未安装。没有调用新实际签名RPC、实际systemd只读沙箱或创建新容器/HDD捕获；6013aa1的实际HDD/业务/页面均待验，不能借前批818396e证明当前整链。

## 保留的失败与修复

新测试第一次把RequestContext.Capability字节类型赋成字符串，CS0029导致测试程序集未编译；随后一次no-build筛选没有匹配新测试，**不计通过**。修成ByteString后专项51通过。首轮全量Unit有1项旧恢复能力smoke仍期望minor51，4335通过/1失败，Integration432/7默认跳过；只同步实际协议版本断言到52，未改旧能力/边界。

复核又发现proto3合法0/false响应不能与缺失混为一谈，补optional存在位和正反例，最终58专项/4343全量全部通过；合法空页计数与complete=false、省略原时间/默认ACL计数等都覆盖，未把旧51通过冒充最终58。Root只读证据收集曾因sudo PATH无dotnet失败，首个猜测路径也不存在；沿实际用户启动器定位固定10.0.402工具链后再核候选来源，原失败记录保留，未修改系统PATH或生产。

root私有忽略原件`deploy/artifacts/acl-history-rpc-candidate-6013aa1/`保存33份冻结源、最初编译/无匹配/失败全量及最终构建/测试/安装预览、当前运行边界和SHA索引；目录0700、文件root:root0600。没有实际capability/凭据进入源码或日志。

## 继续实施

先补原生Probe与同冻结来源安装/依赖/受限unit及真正签名List/Read/HDD、错误来源/重放/取消/精确停止与旧能力兼容验收；再管理员历史目录/API/Vue、当前权限与正常退出。之后才将选择历史绑定到新恢复计划/确认/先留当前ACL原件/独立持久意图与Worker执行及故障对账。递归ACL、可恢复删除/长期清理、维护故障及其余59项/UI/C#酒馆/统一供应商/AI Live2D继续；17项移出不算完成，开发sudo保留，不自动回滚dev或重启主机，整项/阶段完成率不提升。

记录检查：8项范围测试通过，7份变动MD的798条相对链接有效，目标第1节及原reader QA正文逐字保持；原件50份静态SHA核对，git diff --check通过。文档及候选子步骤不提升59项整项完成率。
