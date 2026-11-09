# 退役可信只读采集与物理差异候选

2026-11-09，候选 `b7aa080402e55734963d32256ff955fdac5ddcc9`。新增固定状态/Root-HDD-namespace只读采集库及独立私有stdin入口，复用独立证明模块导出的原始元数据锚定；不改冻结源、产品服务或安装态。

## 采集与边界

固定source/purge/archive/restore-claim/retirement目录，Root私有状态与claims通过canonical/no-follow目录FD读取；JSON限制64KiB，registry有界scandir，总条目在任何JSON读取前校验，且总字节32MiB。每个原始记录保留FD身份、实际字节大小/SHA，关联六份原始元数据和配置/存储身份后才读取live namespace。

只加载四个固定、Root所属/单链接/不可组或其他写的安装态读取模块，不导入退役执行器。ContainerRoot验证当前容器/实例与挂载，实际父目录FD和宿主fixture路径均须对应原计划的parentIdentity；只读取固定recycle-rpc.bin和已知私有暂存目录，检查新inode、字节及xattrs。

HDD target以create_namespace=false/read_only=true打开并反复attest；使用已存在的history.lock，Root600/单链接/空文件、只读FD，非阻塞共享锁，缺失或忙直接失败，绝不创建/修复。整个payload namespace逐文件FD哈希/属性，2GiB总预算、单文件1GiB；QA namespace限制16MiB，xattrs限制32项/4KiB；每块读取检查120秒期限。两次完整采集必须一致，期间配置、目录绑定、锁FD、源session均重核。它是有界观察，不保证未来不可变。

独立physical_delta_proof要求可信before处于Reserved、已观察到留存到期，after为一致的Completed/Retired及有效证书。只有选中的HDD.data消失，其他HDD文件/原始元数据与帧/live副本/namespace保持；前置日志/marker原始字节也须关联，不允许补写或遗漏registry/marker。仍明确rpcAndAuthorizationProvenanceVerified=false，不将物理差异替代签名来源、当前权限或安装态RPC证明。

## 私有入口与复核修正

以python3 -B启动collect_container_recycle_archive_retirement.py；入口在任何项目import之前拒绝缺-B，严格限制一个stdin包并要求EOF，10秒输入/125秒总alarm。库仅将capture处的bytecode标志校验视为sanity check；直接库调用也必须在import前禁用缓存，不能声称事后标志能撤销已产生的pyc。

包只包含nativeRequest和folder，不接受状态路径/模块/时钟/执行动作。capture_native保留父进程所供UTF-8原始行的base64、SHA及长度，明确rpcOriginVerified=false；它不是从Agent截获的原始传输，必须另与真实RPC帧关联。输出仅用于Root私有父进程证据管道，不作为公开日志。

独立只读复核指出pre-import缓存、合计目录预算以及请求字节来源三处边界；已增加入口、读取前合计预算和原始字节标签，复核确认修正后无新增具体错误接受或TOCTOU问题。没有通过自动清理或重试修正不一致快照。

## 实际验证

17新增采集专项首轮逐步加入后全部通过，最终完整回收root **848通过、零跳过（97.712秒）**；14CI合同/8范围/4Python编译及diff检查通过，原17项独立证明也包含在完整回归。本轮未改C#，未重复上批6137 Unit/Release。

使用实际临时FD、记录/marker/hash/xattr、只读open断言与临时unlink，验证预留前缺席不创建、busy锁、源inode/配置/跨pass真实数据变化、符号/硬链接、预算提前拒绝、-B/EOF在依赖导入前拒绝、原始输入精确保留，以及仅.data缺席的差异。完成分支的存储硬件、namespace及到期时钟明确为double，**不是配置HDD、新安装态RPC或自然老化验收**。

实际当前固定retirement目录缺失检查通过：FileNotFound拒绝、未创建目录、未导入安装态provider，未执行Agent/HDD操作。原老化来源另行当前观察仍未到期/未执行/无自动执行，最早2026-11-10 10:00:02.252+08:00，原策略/claim/HDD/实例与f318d9ab部署保持。

准确上批b56ba510 CI运行42564768809已completed/success，12项全成功；本批新CI待终态。旧a576图谱补发失败仍pending。

证据 `retirement-readonly-capture-611dbb1d53e44ae09b640c0550cbf384`：8文件加manifest，SHA256 `8f00cc82994b6594e736e1982e1626ccc200a5945045a9d892d37160b59219e1`。包含4份提交源码、完整848回归、实际缺失拒绝、当前老化观察及验证摘要；Root:Root/0700目录、0600文件。

## 继续点

继续新CI终态、固定helper/session沙箱预算和标准版本/安装门禁，再以真实RPC帧与该采集器验新安装态/实际配置HDD正向、自然到期及故障矩阵。永久业务/当前身份资源授权/共享保护/Worker/持久化/API/Vue/Jobs及59项继续；17项移出不算完成，整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持。
