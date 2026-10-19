# 目录压缩发布内部引擎

## 固定源码复验：3ff90e1

完整提交 `3ff90e1fe0b4b326a65ccc19fa60e753fb7697e3` 已推送dev并快进工作站源码；未安装这些模块、未构建/重启产品运行时。直接由该提交git archive导出的干净公开源码副本执行9组真实Linux测试，56新增+154既有=210项全部通过，随后真实HDD捕获/私有解包及登记根两格式发布探针均通过，精确夹具清理后3类测试目录剩余0。HDD探针differentSourceDevice=false，仅版本留存，不能称异盘容灾。

固定源码[CI39194139091](https://github.com/leng125172-code/whale-deck/actions/runs/39194139091)的backend/native-postgres/installer已成功，frontend仍执行；后续终态单独跟踪，不冒称全绿。线上仍为2a60668/RPC1.29，11核心healthy和两宿主服务active。

本轮 `FileTreePacking` 把登记目录生成新的TAR/TAR.GZ，独立域指纹、32KiB回执、私有临时文件、完整归档及源manifest验证、NO_REPLACE发布和只观察恢复。没有复用旧复制/文件回收签名计划或解释旧回执。

## 真实边界

- 来源要求list/download/archive，目标mutate/upload/archive；源版本/完整manifest、双方根策略摘要、目标父inode与格式入计划。openat2、根别名/包含关系、受保护后代/链接/挂载校验保留；不覆盖既有目标，不删除源。
- 至多10000项/32层/16GiB源内容和17GiB归档，流式完整读取和哈希。容量预留、取消/600秒限制、发布前源全量再次核对、策略撤销/父目录替换防护；临时对象在root/0700私有目录，完成验证后按登记策略owner/0640发布。源非全文件系统原子快照，未来仍须业务层级锁与当前权限栅栏。
- 中断查询/重入不重压缩、不晋升.partial、不自动清理。发布后的完整inode/摘要/元数据吻合才补Completed；未发布且有本回执私有inode证据为Aborted；无创建身份、外来目标或策略/父变更等保留Unknown。原件与证据不自动删除。
- **未新增CLI/helper、gRPC、BFF/SQL/Worker或WebUI，未安装这批模块。** 线上仍为独立UI修复2a60668/RPC1.29；不是用户已可点压缩或完整P2验收。

## 实际验证

- 20项真实Linux root测试通过，ResourceWarning作为错误。两格式/标准tar、中文长名/空目录/二进制、登记owner、完整1GiB+7尾部及全量SHA、源inode/模式/mtime/ctime不变；目标占用/晚占、晚改源、策略撤销、父替换、坏半成品/计划/回执、容量/超时/取消、跨owner/幂等及满额后查询覆盖。
- 既有目录manifest21、普通HDD备份20、单文件历史25、传输30、目录复制26、目录移动32，共154项真实Linux回归通过；与本批22 codec/14留存/20发布共56新增分开计数，合计210项不当作RPC或前端证据。
- 真fork子进程在Preparing/StagingIntent/Created/Staging/Ready/Publishing/Published共7栅栏SIGKILL，父进程确认信号；前六不发布，最后只观察既有完整文件补回执。禁止codec.create的断言证明查询/重入不重做。
- 实机登记根直接探针：两格式各2,097,159字节，独立标准tar完整读取及整包SHA、登记owner/0640、原件全字节/inode/元数据、重复查询通过。回执隔离在自有root/0700夹具内，仅精确移除自有固定文件与空目录，无生产任务/审计清理；报告明确installedAgentRpcOrUserInterface=false。
- 夹具初版中文名360字节，被NAME_MAX拒绝；改为244字节并预先断言<=255后20项通过，未放宽路径检查。先前19项结果不含新增1GiB发布测试，二者不混称。

## 继续完成

独立受限Agent RPC/固定helper/可信加载、签名权限、BFF持久计划/任务、共享层级锁/租约/取消/Unknown恢复、人类审计、API/WebUI和安装态真实身份验收。解包发布、目录回收恢复、ZIP等格式、长期清理仍在完整76项目标内。

旧普通文件传输单文件1GiB上限未改变；引擎17GiB上限**不是大归档已可通过旧下载接口获取的证据**。须补独立有界大归档流式下载，不缩小目标、不改变旧签名请求。

GLM/glm-5.3-flash本轮只读尝试定位3个公开路径；指定Runner.cs不存在，真实定义在FileTransferGrpcService内，返回部分定位被路由判失败。68113ms、57606输入/31488缓存/1151输出、1次失败/未重试、费用未返回。GPT独立核对并负责架构、安全、实现、实盘和最终结论。
