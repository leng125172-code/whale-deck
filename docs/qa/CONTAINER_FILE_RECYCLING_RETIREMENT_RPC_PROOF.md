# 退役 RPC 与 Root 原始字节独立关联候选

2026-11-09，候选 `b56ba510bcf248183fc7ccb37e6cd71aa646b8b3`。新增独立Python证明模块及测试；不改产品/冻结代码、安装态、策略或执行器。

## 证明范围

严格校验20键原生计划/22键protobuf JSON，递归复用原Archive/Purge/Source独立投影；规范十进制int64/uint64字符串保持纳秒精度，拒绝布尔代整数、有损数值、重复/未知字段、非有限数值、错误身份/祖先事实及重算指纹后的非法关系。显式null不是protobuf optional bool；Unknown缺席与present false分别保留为None/False，Reserved/Aborted/Completed不能借缺席通过。

绑定父进程批准的归档来源、RPC请求/回执/operation与job、固定探针actor/trace、规范idempotency key及空capability正文；后续帧可要求context完全一致。关联Root实际JSON的schema、原归档/清理/源生命周期、原claim、历史元数据、配置/存储身份和完整摘要；原始Root文件帧必须600、Root/Root、单链接且实际原始字节大小/SHA一致。

整个Root registry的顶层JSON必须有对应字节帧，不允许省略同源active意图；目录名、空lock及相关marker均检查。核对源围栏与本意图所有权，支持foreign Unknown marker保持源保护而不冒领；拒绝未reconcile的v1 Unknown+Retired状态。v2完成证书必须绑定证书中的执行计划、原归档对象、unlink标记、精确摘要和不晚于独立观察时间的完成时间。

**这些证明只检验可信独立采集结果的一致性。** `originals_before`、`frames_before`、registry/raw frames和observed_at必须由父进程独立采集，不能从RPC回显重建；模块不证明它们的权威性或新鲜度。固定RPC QA路径是`work/recycle-rpc.bin`，不泛签任意业务registry；其他claim文件仅有名称/文件帧检查，不声称逐个语义验收。函数始终返回physicalDataDeletionProved=false、physicalNamespaceAccepted=false，不能以完成回执/摘要代替实际HDD前后差异和命名空间验收。

## 本轮验证

17新增专项通过，完整回收root回归 **831通过、零跳过（93.053秒）**；14前端CI合同/8范围/2Python编译及diff检查通过。本轮没有改C#，未重复执行上批6137项C#单测或Release构建。

专项包括实际临时Root预留/取消日志与原始字节、一次临时unlink后的真实完成证书、未来/篡改证书重算摘要仍拒绝、遗漏registry/替换字节/目录/lock错误、foreign Unknown、完整请求/回执/三态与64位精度。时钟、namespace、存储硬件attestation是明确double，不是自然老化或配置HDD正向验收。反例在临时HDD路径重新放入冲突数据，Root关联仍明确返回“物理删除未证明”，防止把旧证书当作当前数据缺席。

首轮13专项有1失败/1错误：引用了不存在的旧header_proof函数、storageHash反例恰与原值相同。已改为本模块启动头验证及确保反例实际不同；修正后17专项和831完整通过，未放宽政策。原始失败输出在会话回执，包内保留失败摘要；独立只读复核未发现具体错误接受或不安全产品导入，明确提醒独立输入信任边界。新子进程测试确认纯证明模块不加载deploy/host产品模块。

实际重核上批封存探针证据：先验证其manifest固定摘要和4份原始输入/输出/文件打开跟踪的SHA，再用新模块检查启动头唯一、旧1.59拒绝阶段/返回码/公开能力与零操作帧一致，原跟踪未打开签名密钥。**没有重跑探针或新增Agent调用**，不把重核称作新实机执行。原老化来源另行当前重读仍未到期、未执行、无自动执行，最早仍2026-11-10 10:00:02.252+08:00。

准确上批a576ab3f的CI运行42561792228已completed/success，12个job全部success；新提交CI仍待终态。图谱上批补发失败仍保留pending。

证据 `retirement-rpc-proof-1079efa2e18849b286abdd0e49bee8ad`：6文件加manifest，SHA256 `073536696cc9bd05aeac7ea5afd9535224f3eaed5a520f514ab02170cc5efd79`，含2份提交源码、原始831回归日志、当前老化观察、旧证据重核与验证摘要。Root:Root/0700目录及0600文件。

## 继续点

继续可信只读Root/HDD采集器、完整物理before/after验收、新CI终态、固定helper/标准安装及新安装态RPC正向；原自然到期、永久业务/当前资源授权/共享保护/Worker/持久化/API/Vue/Jobs与其余59项仍待。17项移出不算完成，整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持。
