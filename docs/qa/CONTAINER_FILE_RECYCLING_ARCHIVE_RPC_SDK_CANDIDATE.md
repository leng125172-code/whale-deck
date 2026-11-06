# 归档复制：独立签名协议与 SDK 候选

2026-11-06，候选 `3b88a65799290765d834e64ce4c1fd29513a8916` 已编译、测试并完成当前安装版的真实拒绝检查。承接[复制内核](CONTAINER_FILE_RECYCLING_ARCHIVE_COPIES.md)，新增协议/数据校验/客户端；没有映射 Agent gRPC 服务、接执行 Runner/Root transport、增加新 capability、升级安装或接永久业务页面。不把本批记作正向签名 RPC/HDD 或完整恢复验收。

## 明确绑定与新副本语义

新增独立 ContainerRecycleArchiveService 五个 Unary：Plan、Reserve、Apply、Inspect、Discard。命令固定 ArchiveCopy，绑定原 purge operation/owner/完整 purge plan、原回收 source 和 HDD 历史摘要；新执行 ID 必须与原 source/purge 不同，并与本次 context JobId 相同。Application 使用自身 DTO/接口和校验，不引入 Contracts 依赖，不增加 schema 或本地身份体系。

请求摘要分域并覆盖 descriptor 全名及完整 Protobuf 字节；独立 capability domain 绑定 expires、nonce、完整 RPC 方法和 requestHash。CapabilityValidator 拒绝通用旧签名、跨方法/服务、坏摘要、过期/超期与 nonce 重放；拦截器已加入独立分发，候选 Policy 再以恒定时间核对 header 与实际消息。SDK 在 capability await 前冻结 Protobuf，包括深层原始文件属性字节，不能通过等待期间修改输入改变已签载荷。

计划将原已删除身份、当前存储配置/目录/归档身份、完整来源/清理/marker/历史摘要、当前父目录版本及可缺席私有目录全部冻结。私有目录 null 与存在是两种状态，不能用零 inode 代替 null；返回命令须与请求完整相同，指纹重新计算。SDK 另拒绝重新计算外层指纹来掩盖损坏的内层 purge 指纹。C# canonical 与 Python 原生 digest 直接互验 Unicode、超过 2^53 的纳秒整数和两种私有目录状态，不经浮点转换。

回执严格关联本次/原 source/原 purge 三个 ID 与指纹。Completed 的 publishedIdentity 必须是目标父设备上的新 inode，排除原 source 与 HDD inode，UID/GID/mode/单链接/大小/mtime 与来源相符，changedNs 的存在状态保持。Unknown 的 archiveCopied 必须缺席，当前绑定可缺席或 false，不能伪成 true；重复/未知字段、损失精度的数字、错误身份及未发布状态都拒绝。

这里特意保留内核语义：Completed 是本操作 durable journal 已证明的历史复制结果。以后若来源 claim 出现冲突，不能把历史 true 抹掉；OwnedFence=false 时只能 bindingCurrent=false/null，绝不能 true。新增真实临时复制/冲突 claim 回归以及 Wire/SDK 用例共同验证，旧 Root/负载和对方 claim 保持。此规则与“原清理成功必须持有当时围栏”的 purge 回执并非同一状态模型。

## 版本与可用性

SDK 要求 major=1、minor>=59 且显式 docker.files.recycle.archive；1.58、错误 major 或缺能力时不发送归档操作方法。Agent 当前源码继续报告 1.58 且不宣告此能力，没有提前 Ready。DI 注册的客户端接口不等于新业务入口；没有新 API/Vue 按钮或 Worker 写入路径。

后续需完整接上 Agent gRPC/Runner、固定白名单 Root transport/Invocation 停止与安装清单，再推进 1.59 及原生正向探针；不能只加 capability 或借 purge 能力执行归档复制。

## 测试与原失败

5752 项单元测试零跳过通过，其中新增 Archive 专项 102 项；782 项集成通过，7 项按默认原生依赖条件跳过，未计通过。另 165 项关联 Linux root 检查零跳过（含新增 Completed/foreign-claim 原件回归）、14 CI 合同、8 范围通过；编译无 warning/error。SDK 的 HTTP/2 回执、签名和变更用例使用明确测试 handler，不冒充实际安装态写入。

初版专项 94 项中 68 通过、26 失败；完整原输出和初始源码已保存。原因是复制测试夹具遗漏若干实际字段变异，以及新副本错误沿用原 source 设备（应为目标父设备），导致严格回执拒绝；补齐变异、纠正新副本夹具后通过，没有放松校验。另保留新增 ByteString 测试缺 using 的 CS0103 事实，以及采集原测试日志时 runuser PATH 缺 dotnet 的启动原输出；改用用户的精确可执行路径后重新采集原 26 失败，再修正。没有将采集启动失败计作协议检查通过。

## 当前安装版的真实兼容拒绝

冻结 16 个改动源码与 52 个 Release 探针/依赖文件。unit `whaledeck-qa-archive-sdk-3b88a657`，Invocation `078cb0b73fac4a5fa5821316eeedf195`，原 journal 业务成功与 Deactivated successfully；ProtectSystem=strict、ProtectHome=read-only、PrivateTmp/PrivateNetwork、256 MiB/30 秒，仅本私有证据目录可写。

候选 SDK 连接真实旧 Agent，实际报告 protocolMajor=1/protocolMinor=58 且没有 archive capability。直接 GetCapabilities 及 SDK Require 内部检查后返回精确 Unimplemented/CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE；没有调用归档 Plan/Reserve/Apply/Inspect/Discard 方法。凭据配置必须为指定名称的不存在私有测试文件；若已存在探针先拒绝，因此没有读取实际 capability key。结果明确 oldProtocolRejected=true、sdkRequireRejected=true、realCredentialRead=false、archiveOperationMethodsInvoked=0；不是通过模拟响应证明旧版拒绝。

前后完整基线相同：53 回收/29 恢复/17 清理永久意图完整行、活动 0，回收 Root124/HDD127/恢复 Root80/目录 Root98/purge Root34、2346 保护、11 核心与生产9694af1c保持；没有创建新来源历史、意图或生产归档 namespace。两个宿主、应用和运行部署未覆盖。

证据 `container-recycle-archive-rpc-candidate-186fd5d713a24115b773b39ca1608915` 共 99 文件另加 manifest，摘要 `13cd969041ed6dd322c6a52b77ca229c207cb8b3b4b1d7f5633579e09fd69ec2`，Root 0700 目录/0600 文件保持。准确上批 d955faa2 的 CI42235440080 已核对 12 路全 success；本批新 CI 仍需按新提交核对，不借用旧绿灯。

继续服务端执行适配、Root 传输/标准安装、正向原生签名/HDD验收，再做永久业务/共享保护/真实页面、完整故障矩阵、成功 claim/暂存清理与留存/维护连续性及59项。17项移出不算完成；整项0/59、阶段0/6、工程量粗估25%–35%、酒馆/Live2D功能0%保持。
