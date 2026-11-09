# 退役父进程节拍探针与安装态否定门禁

2026-11-09，候选 `a576ab3f021cb3e6e7e2f19e6be2f34168200f32`。新增TerminalProbe独立`container-recycle-archive-retirements`模式。仅准备QA探针，未改Agent映射/能力、helper、安装器或生产服务；运行仍f318d9ab/1.59。

## 探针约束

固定本机UDS，启动头严格绑定运行/探针revision、目标容器/实例与核心排除ID，校验探针自身程序集InformationalVersion。实际GetCapabilities必须major1/minor至少60/精确专属能力全部满足，才读取Root签名密钥；公开版本/能力布尔可输出为证据，密钥、capability token/签名头不输出。

输入逐字节有界读取：启动头8KiB、命令64KiB、必须LF，拒绝CR/截断/超限；最多64命令、总寿命12分钟。首个已拥有的Archive请求固定后不能换来源，祖先容器/实例及`work/recycle-rpc.bin`路径绑定，源UID/GID1000、单链接、16MiB预算与祖先指纹校验。无source/purge/archive创建客户端，没有生成或删除旧来源的命令。

父进程显式提交Plan、核验结果后才发Reserve/Apply/Inspect/Discard。只接受本进程审批过的完整计划，退役ID与archive/purge/source三代分离，新job/key保持固定；Reserve只在unreserved，Apply只在Reserved且在await前记录已发送，禁止重复Apply。每次返回严格回执再等待下一命令；quit/异常均不自动清理、取消或重试。Plan成功后额外验证同一authority重放被拒绝。计划/请求/回执只通过Root私有父进程管道供独立物理证据关联，不能把stdout当作可公开日志。

## 本轮实际验证

35项新增探针纯测试、退役联合 **225通过、零跳过**；全量C#Unit **6137通过、0失败、0跳过（34秒）**，Release全解决方案0警告/0错误，14CI合同/8范围与diff检查通过。覆盖单次Apply/迟到Reserve拒绝、祖先ID、年轻计划不提升到期、重新哈希的错误事实、未知字段、先正向验证的owned scope反例、UTF-8字节预算/不消费下一行/取消输入。

首轮Probe构建CS8602：Require助手未向编译器声明失败不返回，补DoesNotReturnIf(false)空值流标注后修正；首轮Unit编译xUnit2013已改Assert.Empty。没有放宽安全条件，失败轮未记通过；原始失败输出在会话工具回执，包内只保留失败摘要，不冒充完整原始日志。独立只读复核在“不输出秘密签名凭据、允许公开版本观察”范围下未发现功能阻断项；未替代实际正向协议验收。

已按提交revision重新publish，并把探针及所需10.0.13运行时复制为Root私有只读快照：204文件/86,983,594字节，低于200MiB复制预算，逐文件摘要重核。实际运行只发送绑定探针revision的占位目标启动头和EOF，**没有任何操作命令**；不借用/引用真实老化来源执行动作。

实际旧安装态拒绝通过：退出1、stdout0字节，phase=capabilities，code=CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE，实际major1/minor59/retirementCapability=false。对子进程仅跟踪openat/openat2，确认`agent-capability.key`从未打开；未跟踪网络数据或密钥内容。该证据只签旧版本在key/操作前拒绝，**不签新能力可用、安装态正向RPC、真实HDD退役或当前资源授权**。

探针前后原老化来源独立观察均通过且一致：未到期、未执行、无自动执行，最早仍2026-11-10 10:00:02.252+08:00；原冻结Python、策略、claim、实例、HDD和生产部署保持。只有本轮私有临时publish输出在完成Root冻结后按精确路径移除，Root证据快照保留；没有清理业务来源或历史。

准确上批292e8bec的CI运行42560366644现已completed/success，12个job全部success。旧Gateway/native两条图谱pending均按原eventId成功补同步；本批新CI仍待完整终态。

证据 `retirement-probe-25cbfc672fae4fcc9b8b8468481d80c4`：220文件加manifest，SHA256 `584ee0ae068f0d192a8e5ee91f43bd243442304c07b55055c3fdbd305bbc22f2`。含4份提交源码、完整Unit/Release/publish日志、前后老化观察、原始拒绝输出与文件打开跟踪、输入/证明/验证摘要及204个冻结二进制。Root:Root/0700目录，普通文件0600，私有dotnet执行入口0700。

## 继续点

继续新CI终态、独立Root/HDD协议证据工具、固定helper/session沙箱预算、标准版本与安装门禁，再验新安装态RPC和自然到期受控单次退役。永久业务、当前身份/资源授权、共享保护/Worker/持久化/API/Vue/Jobs与其余59项仍待。17项移出不算完成，整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持。
