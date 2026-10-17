# 目录复制 Agent 安装态验收

2026-10-17。实际运行时源码 `e0efb8ec736848d9f836f85251c387064399e2e6`，Agent 协议 **1.27**。本批只签独立目录复制 RPC、固定 helper 和安装态完整复制；**不签 BFF、Worker 持久目录任务、人类审计、用户确认或 WebUI**，也不代表 P2-03 或全部 76 项完成。

## 源码与门禁

- `FileTreeCopyService.Plan/Apply/Inspect/Discard` 与 `files.tree-copy-intents` 独立于旧普通文件契约；只传登记根、相对路径、原版本、完整清单摘要、数量、总字节和 UInt64 父目录身份。不经 RPC 传完整清单、宿主绝对路径、UID 或命令。
- 固定驱动只加载 root 所有、单链接、不可组/其他用户写的两个指定模块。两端分别校验 `files-tree-copy` 及原读取/写入授权；只升级精确 1.26 默认根，收紧或自定义策略不变，旧策略另存 `.pre-tree-copy.json`。
- 目录操作独立使用 16 GiB 文件上限、384 MiB 内存、128 FD、1 CPU；旧上传/变更仍保持原 1 GiB/128 MiB/64 FD/半 CPU 预算，不因新增能力扩大旧通道。
- e0efb8e 的 CI **39031927056**：backend、installer、native-postgres、frontend 均 completed/success。Release 894 Unit/118 Integration，本机 6 个原生 Fact 明确跳过；176 项实际 Linux 文件测试、218 前端单元与既有 136 浏览器回归通过。
- 同一冻结源码的工作站非超级 PostgreSQL 报告 `dispatch-441472f514054dfcb1fba50806d3baa2.json`：2026-10-17 12:19:48.312412–12:21:21.232920 UTC，执行 1/通过 1/cleanupConfirmed=true，固定 SDK `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。没有把跳过当成原生通过。
- 随后的 c966d9e、c1666c5、2ae4184 只修验收诊断/探针/测试，不改变 `src` 或 `deploy/host` 的实际产品代码。新串行 Release 零警告/错误，**898 Unit/118 Integration** 通过；3 项诊断测试通过，6 个本机原生 Fact 仍明确跳过。最终 QA 2ae4184 的 **CI39034546059 四作业均 completed/success**，installer 日志明确 176 实际 Linux 文件测试及新增 3 诊断测试全部 OK；没有用前版成功代替新源码门禁。

## 实际覆盖发布

发布前工作树精确 e0efb8e 且干净，11 核心 healthy；活动上传/变更/回收/路径锁/终端全部 0，已装根策略等于精确 1.26 默认。仅调度一次固定平台更新，未因观察超时重新启动，也未自动回滚。

实际更新服务 InvocationID `3c7679558b0c4864b7a4616900632eca` 在 **20:39:50 CST** 进入 `Deactivated successfully`，Result=success/ExecMainStatus=0，耗时 1 分 51.792 秒。Agent 与维护宿主校验通过；Agent 程序集含完整 e0efb8e，默认新根策略及旧策略备份均独立比对一致。宿主 .NET/ASP.NET 和 PKCS 都为 10.0.13。

API/Worker 容器程序集未嵌入 Git SHA 的既有缺口未在本批修复；不把宿主 SHA 校验套用于容器、不声称镜像已具备完整来源签名。统一构建来源标记继续列入 P0/P5。

## 已安装通道探针

最终探针源码 **2ae4184b2d7344a6dba955ed208ed75edbf64a1e**；实际产品运行时仍 e0efb8e。SDK 客户端为冻结源码 self-contained 构建、固定镜像身份；执行容器无网络、无 capability、只读根，仅取得 UDS 和只读短时签名所需密钥，不挂 Docker Socket 或业务数据。密钥只在进程内存使用并清零，不输出。

已实际证明：

- 原安装态 1.27 能力、显式两端根授权；原方法签名/nonce 只消费一次、错误方法拒绝、错误 owner 不可查回执、旧源版本与被篡改计划拒绝。
- 含中文目录/二进制和空目录的 **2,097,159 字节**完整复制；原操作 Inspect 与重复 Apply 返回同一持久结果，目标已存在时新计划拒绝，不覆盖。
- 先 Discard 再 Apply 得到同一 Aborted 取消栅栏，迟到 Apply 不创建目标；保护名 `.env` 的整树计划被拒绝。
- 含 **1,073,741,831 字节（1 GiB＋7）**单文件和空目录的真实复制通过，证明新系统预算没有被旧 1 GiB LimitFSIZE 截断。该单次验收不表示生产已实测完整 16 GiB/10,000 项上限或断电全部组合。
- 独立宿主观察者完整读取源和目标 SHA256，核对长度、普通权限、纳秒 mtime、目标登记 UID/GID、单链接和不同 inode；空目录保留，源保持。两个 Completed 和一个 Aborted 的 root 回执实际存在，均 root/0600/单链接且原操作/源计划匹配。
- 最终夹具 `wdv-agent-tree-4e23b7a175cc4502` 的成员、设备/inode、所有权、无链接/挂载均精确核验后删除；两个存储的 SDK 容器 ID 在标签匹配后删除，最终输出 `exactOwnedFixtureRemoved=true`、`ownedSdkContainersRemoved=true`。永久 root 回执保留。

## 失败记录与修复边界

1. 初始探针只收 stdout，遗漏 Program 在 stderr 写出的脱敏错误。c966d9e 加入两通道白名单结构化诊断及 3 项测试；不输出原始日志、异常或多余字段。
2. 原探针错误预期 capability 拒绝为 Unauthenticated；现有拦截器一直是 PermissionDenied。c1666c5 修探针并补四个方法的实际拦截器单测，**没有放宽鉴权或改产品错误契约**。前两轮夹具没有目标或 root 复制回执，经精确清单验证后移除。
3. 第三轮复制已经 Completed，但随后独立下载漏传原有必需 ExpectedVersion，RPC 按原规则拒绝。2ae4184 先 Inspect 再携带版本下载并增加细化阶段。先前 Completed 回执 `1604a856467640eda3bb5601c97852b7` 保留，独立核对真实 2 MiB＋7 字节/权限/mtime 后才删除该轮夹具；不把那次失败探针整体记成通过。
4. 首次人工根策略读取漏 sudo 而 PermissionError，随后用 root 只读核验一致；首次旧 Linux 代码夹具清理过严地要求 source.tar 为 root owner 而拒绝，确认它是原 user 所有单链接传输包后按精确路径/设备/inode/owner 清理。没有改目录权限或删除不明对象以绕过检查。

59 个自有 Linux 单测代码文件、其精确 `/tmp` 目录和传输包在设备/inode/所有权/类型/无链接/无挂载验证后移除；都是可由 Git 恢复的公共代码，不是业务数据。

## 覆盖后兼容回归与最终状态

通过真正短时 Authentik 管理员、Cookie/CSRF/Origin、维护入口/Gateway/API/SQL/Worker/UDS/已装 helper 顺序复验：34,603,015 字节上传/下载/7 秒暂停/覆盖/幂等/取消/审计；65,543 字节单文件 copy/move/chmod 和单叶 mkdir；UTF-8/BOM/CRLF/编辑与 HDD 完整旧版本；普通文件历史/回收/不存在及覆盖恢复/只清选定 HDD 版本。四脚本全部成功并精确清理，永久 Job/审计/目录/root 回执保留。文件变更输出中的 directoryCopyMovePermissions **不是目录树 BFF 证据**。

最终独立核实：11 核心 healthy，Agent/维护服务 active，临时身份 0，活动上传/变更/回收/路径锁/终端 0；四轮精确探针夹具和全部对应 SDK 容器均不存在。API/Worker 仍无 Docker Socket 或宿主数据目录挂载，开发 sudo 保留；没有改 KDE/网卡、重启 Docker/主机、填满磁盘或自动回滚。

前端未新增控件，HTTP 与 Gateway 的 FilesView JS/CSS SHA256 和前版已测资产一致；本批没有新的真实浏览器目录复制验收，现有浏览器门禁也不证明尚不存在的目录任务 UI。源与目标同设备，本批复制不是异盘备份或灾备证明。

## 下一步

继续独立 BFF 异步计划、短时原登录确认、永久任务/Outbox、6 秒租约与撤权/取消监测、共享层级锁 owner 分支、人类审计和 WebUI；长目录散列不放进同步 HTTP 请求。原任务 RootStarted 后只查询/Discard 找回结果，不自动重放。Unknown/私有部分对象保留，显式处置不得伪造成功；完整目录移动/回收/归档、容器文件、租户隔离和长期清理仍需实现。完整 P0–P5 的 76 项保持 active。

本轮 GLM/glm-5.3-flash 仅尝试只读定位旧任务入口，240602ms 后超时，无有效交付；1 次失败/0 重试，输入/缓存/输出/费用未返回。GPT 接手定位并独立完成发布、诊断、修正与验收；未将安全判断或部署委托第三方。
