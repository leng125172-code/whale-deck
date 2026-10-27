# 容器上传历史观察与永久封存候选

日期：2026-10-27。单工作站59项中的容器文件写入子范围，**未安装/未开放业务入口**。线上仍为f5a0029与Agent minor40，本批协议minor41仅源码候选；没有据此签收完整文件模块或59项。

## 实现与边界

- 原Root驱动所有动作先进入原容器namespace，停止/重启后无法观察历史。新增独立ObserveRecovery/SealRecovery签名方法及minor41专属`docker.files.upload-recovery-seal`能力，要求同时具备bound-uploads和取消栅栏。原五方法保留；只有StageUpload为客户端流。API/Worker没有新公开入口。
- Root历史观察只读取root所有0700目录内、0600/单链接/有界且无重复JSON字段的原操作journal，按完整CID、原纳秒实例、operation、owner、目标证明绑定。不进入Docker/进程/目标路径，不把缺失存储错误当Missing。不返回owner、原路径、登录、凭据或原始私有回执。历史Published不等于当前版本证明，bindingCurrent始终false。
- 观察摘要独立域绑定全部历史事实及隐藏owner；原始记录摘要保存全字段。C# Agent与SDK均校验精确输出、固定状态、大小写/标识/密封一致性及观察摘要，Python/C#黄金向量一致。新RPC签名分别绑定原上下文和当前恢复主体/Trace/新恢复Job；不改写原身份。当前人类授权、二次确认、永久恢复任务及审计仍须在Application/SQL实现，不能拿底层RPC替代。
- 封存同一原操作flock，活跃Stage/Publish/Capture不能被中途接管。只有与已确认观察摘要一致才原子写schema3永久封存，原journal完整嵌入、原摘要与当前管理员/Trace哈希/新恢复ID持久保存；幂等重放只接受同一固定请求。所有暂存字节、私有输出、旧inode与HDD历史均保留。封存是接受历史不确定性，不假称上传成功或未写入。
- 没有使用旧writer会忽略的sidecar：原journal在同一锁内替换为schema3，而旧minor39/40 reader只接受schema1/2；迟到的旧Stage/Publish/Capture在取得锁后也拒绝。专用fixture保留c521c39原minor40的精确receipt方法（方法SHA256 fd1552191f15ca2ed0786a8bcc8fcb5b1dc3d220fa4409d1425d3c321e93de44），不是改旧断言绕过兼容。
- wrapper大小沿用16384字节上限，超过时保留原journal拒绝封存。没有目录递归删除、恢复文件、清理HDD或释放SQL保护的底层捷径。只能通过固定helper枚举动作/private stdin运行，拒绝任意路径/命令。

## 验证

- Release零警告错误；全量2561 Unit/174 Integration通过，默认7原生夹具明确跳过。新增SDK/Agent/内容签名检查包括旧minor/缺能力拒绝、原身份与新管理员分离、修改请求体/方法/重放拒绝、输出摘要/状态/范围不一致拒绝。首轮精确七方法与五处旧协议版本预期失败，按新契约更新到精确7方法/minor41并保留全部旧方法/能力检查后重验通过，没有移除测试。
- 14项新真实Linux-root测试通过，含原实例不可用时历史读取、owner/实例/目标证明隔离、观察变化拒绝、保留完整journal/字节、重复/不安全JSON与链接拒绝、16KiB封装边界、真实跨进程Stage持锁，以及封存前/后实际SIGKILL。新/冻结minor40 reader及迟到Stage、Publish、Capture、Discard均拒绝封存；当前文件与HDD字节/inode保持。全体容器文件原生180项通过。容器会话是明确的合成夹具，不算产品授权或安装态证据。
- 扩展实际systemd profile验收通过：冻结根所有模块/固定helper、生产同款沙箱与能力配置，使用有标签且无网络的独立QA容器/独立state/HDD命名空间；完整2097159字节发布、旧inode/HDD全内容、精确InvocationID取消和原取消栅栏通过。真实停止/重启该容器后，普通旧实例Inspect拒绝而历史Observe保持同摘要；Seal完整保留原回执及所有文件，重复请求不变；当前新实例Missing先封存后合法完整Stage/Publish均精确RECEIPT_INVALID且零新payload。
- profile正常结束，临时容器/目标目录/历史与活helper精确清理通过，独立标签查询无残留，11核心容器逐项healthy。没有安装候选Agent、没有更新API/Worker/Gateway，也没有跑新功能真实SSO/UI。
- 私有证据保存在`deploy/artifacts/container-upload-recovery-20261027/`（0700，文件0600）；profile保留根模块SHA256。关键上传模块54a701a53f4f6c6869c8d24b8de23937ac66527735554e15f0041798640b12d7、固定驱动f1b52be9ab557743176e4e80f77d589e967a7963ed9d4f42fad2fdf792fe9e5a。12宿主运行包检查、8范围一致性检查通过；前端本批无修改，不重复跑模拟页面来替代底层恢复验收。

## 下一步

独立容器recovery kind/永久计划与二次确认、新管理员授权/租约/审计、同事务确认原Unknown版本及sealed证据后解除共享发布保护；不能落入宿主move分支或把schema3拒绝当普通失败续发。接入后再完成HTTP/Worker/精确代理及真实Cookie→SQL→Agent→Root→HDD/桌面手机验收，最后覆盖部署。当前不把已有原生PG旧提交证据冒充新恢复SQL通过，不签安装态RPC或页面。
