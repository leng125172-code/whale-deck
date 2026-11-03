# 容器ACL历史读取：818396e候选与真实HDD分项证据

2026-11-03。**已实现独立只读历史校验/分页内核，15项Linux-root专项和431项既有容器文件回归通过；818396e在新独占实际容器/HDD完成5捕获、5查询、7页及准确清理，891acf4独立复核通过。** 这是恢复来源的前置组件，不是ACL历史恢复执行、已安装RPC/API/Worker或页面。产品保持0478262/协议1.51、Agent315250/MaintenanceHost315245，11核心身份/健康保持，无新安装、发布、迁移或宿主重启。

## 实际缺口与实现

已有`container-acl-v1` HDD元数据保存每次修改前完整access/default ACL及原inode/版本/普通属性摘要，但现有Inspect只对账原操作，不提供历史快照。文件内容历史的namespace、schema和业务DTO与ACL不同，不能混用，也不能把Unknown处置当恢复。

新增`whaledeck-container-acl-history.py:ContainerAclHistoryReader`，构造时必须拿到同一真实ACL引擎/历史存储，并精确验证固定namespace及摘要域。只读`listing`和`read`接受已固定当前ContainerRoot的会话，没有CLI/main、任意宿主路径、ACL写入、自动恢复、创建/清理或sudo动作。候选尚未加入安装清单或Agent能力，不能由现有产品RPC直接调用。

每次查询校验当前containerId/instanceId/规范相对路径及保护路径，开始、扫描、返回前复核session/targetProof。路径必须明确且非空，不支持枚举任意容器全部历史。历史实例与当前实例分开返回，同CID旧实例可作为历史来源；这不是同CID实际重启或跨CID历史迁移已验收。

HDD必须先通过既有配置、实际挂载/UUID/设备及root私有目录证明；只以read_only打开，不创建namespace。**只有已证明HDD基目录下确实没有ACL子目录才返回空**；配置错误、挂载/UUID/HDD不可用、目录不安全或EIO不伪装成“无历史”。

单条快照通过Root FD/O_NOFOLLOW、regular、单硬链接、私有权限及16KiB预算读取；严格校验schemaVersion/resourceType/storageId/operationId/owner、完整原ACL请求与计划指纹、原源身份/模式/另一ACL/mask和historyDigest重算。读取前后对比文件身份，拒绝目录内容替换。`read`还必须匹配预期历史摘要和当前请求CID/准确路径，其他CID/路径不返回正文。Root私有文件和SHA校验是存储信任及一致性证明，不冒称密码学签名或防恶意root篡改。

`listing`只返回捕获ID/摘要、时间、源实例、原修改ACL类型、对象类型、原模式及条目数量；选择单条后`read`才返回修改前两类ACL的深拷贝。原owner、完整plan、targetProof、原inode身份、源文件属主UID/GID不返回；ACL条目本身所必需的数值用户/组ID仍保留，不能省略成不完整权限快照。

## 分页、预算与隔离边界

- 单页1–50条，单次最多扫描200候选，目录至多10000条元数据及64个非元数据项；响应条目预算128KiB，单次5秒。超预算拒绝或返回明确续页，不无限扫描或写高频日志。
- 库存版本绑定当前CID/实例/路径/targetProof、目录身份与所有规范元数据文件的stat frame；读文件产生的atime不作为权威。后翻必须同时传after和expectedVersion；页末重扫与重新attest发现新增/删除/替换/原地改写即拒绝，不拼接两个版本的分页。
- 不匹配的有效记录会推进cursor，不进入entries；坏记录计invalid而非混入快照，单条读取则拒绝。响应容量不能容纳单条时明确报错，避免空游标无限重试。
- **这是按CID/路径的条目正文隔离，不是独立租户库存。** namespace/库存版本仍全局；scannedCount、invalidCount、cursor和分页失效会受其他CID记录影响，数量/活动有粗粒度侧信道。后续仅授权管理员查询，不能把这些计数当本容器私有数量；若将来开放受限资源用户，需要额外分区索引或收敛返回统计，不能凭此签严格多租户隔离。

## 自动测试及真实依赖

| 检查 | 实际结果 | 能证明的范围 |
|---|---|---|
| 新Linux-root专项 | 15通过/0跳过 | 真实setxattr及原ACL捕获、只读不写、缺namespace、完整快照/脱敏、跨CID/路径/摘要拒绝、版本分页、坏计划/元数据链接/权限/大小、attest/EIO、会话撤销、并发目录变更与预算；挂载/UUID/ContainerRoot是明确夹具 |
| 既有容器文件Linux-root | 431通过/0跳过 | 旧读写/权限/ACL等受影响回归；不计整个后端全量 |
| 新源码语法与CI接入 | 通过 | 新专项显式sudo运行，避免默认跳过；root库/主验/独立验源码编译检查；不宣称新GitHub CI终态 |
| 实际候选主验 | 首次通过 | 冻结818396e的10模块，非核心新容器/专用HDD及真实ContainerRoot，不改生产安装文件 |
| 独立实际HDD原件复核 | 通过 | 891acf4验证5份HDD与原Root回执、分页和准确清理；重跑只读验证不重新写捕获 |

实际新容器使用固定已有镜像、1000:1000、无网络、只读根、准确自有RW bind、资源预算及标签。已有ACL引擎生成文件access修改/no-op/mask和目录default添加/清空共5次Completed、5份真实HDD元数据；新读取器逐一返回**旧快照而非新目标ACL**，错误摘要拒绝，无原Root回执变化。两份文件每份2097159完整字节、原inode/属主/mtime及普通xattr保持；只有已明示的ACL及相应mode/ctime变化属于原写引擎。

实际全局目录分页扫描110候选，7页返回本案5条（文件3/目录2），无重复/其他CID正文、invalid0；110是全局累计扫描数，不是本案历史数量。查询后完整物理清单及Root字节保持。5份捕获前镜像与备份摘要由独立进程从实际HDD再次读取，并核对原请求/Completed回执/结果；目标CID和准确源均已不存在，11核心及两宿主PID/来源/健康与进入时一致、SQL空闲。源与HDD同device，**不计跨盘恢复证明**。

没有真实用户登录、业务SQL意图/审计、签名RPC、Agent安装、Vue新页面或实际历史恢复。真实普通/匿名用户的权限界限须在后续API接通后验收；不能把候选会话夹具或开发root身份当作人类授权。

## 原件及后续

root私有忽略目录`deploy/artifacts/container-acl-history-c58c9b4016604b52b42af19cefcc99a6/`保存42份静态SHA：冻结10模块、主/独立验证源码、原Root记录、5份HDD副本、查询/分页/物理清单、15专项/431回归及实际原日志。目录0700、文件root:root0600。主验source818396e0cba95563163bbad7b2178ef35227be3f；独立验证source891acf4800838b85f9fb11af1dca69e01526c806。未删除或改写原HDD捕获，凭据不入Git或日志。

本批新专项/主验无失败，旧记录和已完成实际写入未重复执行。首次Git推送发生GnuTLS握手中断，正常重试后成功，未弱化TLS或改变远端；只读复核补齐可重复运行时复用相同冻结字节，不重写生产历史。该网络事件不当作产品功能故障或验收通过。

下一步接专用有完整请求绑定的签名List/Read RPC、最小只读传输/安装、C#历史DTO/管理员授权与有界浏览；再做显式选择历史→当前新预检/确认→先留当前ACL历史→独立持久恢复任务/Worker/UI及失败对账，不能把普通ACL Apply或Unknown Inspect/Acknowledge直接冒充历史恢复。递归ACL、可恢复删除/长期清理、安装态维护故障和其余59项/UI/C#酒馆/供应商/AI Live2D继续；17项移出不算完成，开发sudo保留，P1/P2整项与规划完成率不变。

文档复核：8项范围测试通过，6份变动MD的791条相对链接无缺失，目标第1节逐字保持，4份新Python源码以警告即错误编译通过，git diff --check通过；未重跑前后端全量或宣称新CI全绿。

独立补核：逐一比较5次实际保存的前后完整清单/原计划，原inode、属主、mtime、非目标对象和另一ACL均保持，目标ACL及mode与计划一致；原42份SHA再次通过。补充attribute-preservation-followup.json及单独SHA保留，不改旧原件。
