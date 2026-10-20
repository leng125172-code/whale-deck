# 目录清理安装态子门禁（2026-10-21）

状态：子范围通过，完整文件管理和全部 P0–P5 的 76 项仍 active。当前 Agent/MaintenanceHost 为 `56101c8a93bc477a71f73055db348b85c315aef2`（RPC 1.36）；API/Worker/Gateway 保持 `2c8d1bb`。客户端/独立观察器冻结为 `6c60ea35189d66d357c37c0fcc5bd8798f5aa963`，与运行版只有验证及 CI 差异，不把客户端来源冒充运行来源。

## 实际安装与来源

- 从干净 dev 检出和固定摘要 SDK 镜像发布宿主产物；仅宿主 Agent、维护入口和 helper/默认登记升级，没有构建或替换 API/Worker/Gateway 镜像。本次未修改数据库数据、Authentik 配置或网络。
- 安装器完整校验 SHA256 清单及 self-contained 运行时。实际 Agent 与 MaintenanceHost 的 Microsoft.NETCore.App、Microsoft.AspNetCore.App 和 Pkcs 均为 `10.0.13`。两个实际程序 `--build-info` 的 sourceRevision 均独立核对为 56101c8。
- 宿主验证器实际通过 systemd 自启/运行、UDS gRPC、目录/密钥/模块权限、固定 helper、Docker 访问和当前动态发现的 IPv4 入口；两服务 NRestarts 均 0，11 核心容器 healthy。没有增加 Agent TCP 入口。
- 当前根登记完整匹配新默认；`.pre-tree-purge.json` 独立比对匹配冻结的 1.35 默认。仅已知默认升级；没有对自定义配置自动扩权。

## 签名 UDS 与独立磁盘证明

- 测试源码用 `git archive HEAD` 构建 Linux 客户端，不向构建容器挂载含私有环境文件的检出。首次直接执行无 executable 位的既有发布脚本被拒绝，未创建客户端构建；随后显式用 bash 正常发布，并非超时重启或运行版失败。
- 新独立观察器先验证实际运行来源、全部 13 个固定 helper/模块的完整源码 SHA、登记根、容量、可信客户端权限；只新建 `wdv-agent-purge-33064f5475c64236` 自有夹具。客户端不直接访问业务载荷或捕获私有数据路径，只通过现有签名 UDS 和固定 helper 操作；签名密钥仅内存读取并在 finally 清零。
- 实际生成 Trash 和 Overwrite 两类捕获；每类分别有 2,097,159 字节与 1,073,741,831 字节原件。删除前第一个独立检查点，对四组完整原文件 inode/长度/模式/mtime/全 SHA、捕获和原任务绑定、HDD 清单/归档身份与完整 SHA，及独立 TAR.GZ 全量解码进行验证，再给客户端继续信号。
- 两个小捕获先执行 Archive。第二个检查点确认两归档确实消失、对应墓碑正确、两组原件仍完全相同，大捕获与原任务/清单均未改；随后才批准小捕获 Originals 和大捕获 All。
- 总计 6 个 Completed、6 个 Aborted；实际覆盖缺 capability、复用恢复/回收 capability、错误指纹、跨 owner 查询、Inspect nonce 重放、Discard 后 Apply、Completed 后 Apply/Discard/Inspect。错误指纹没有生成 root journal。三个范围严格区分实际删除事实。
- 最后独立确认四组原件/归档消失、相应墓碑精确指向本次清理、原回执和清单保持、可见恢复输出全 SHA/权限未改、隔离命名空间无残留。客户端成功不是唯一 oracle。
- 本次四组 `differentSourceDevice` 全为 false，实际来源与归档均在同一 HDD 文件系统；不宣称 NVMe→HDD 异盘、物理断电或已安装强杀验收。旧内部引擎的 6 SIGKILL 证据仍只属于临时 Linux 测试。

## 精确清理与私有证据

- 观察器只删除该新夹具的固定七个成员及精确 ID 集合的测试载荷、元数据、清单/锁和临时 root journal；不递归 shell 删除，不碰旧失败夹具、生产文件或任何业务 SQL/audit。额外/嵌套对象、硬/符号链接、非 root 私有回执、身份变化、非空私有命名空间或挂载点均拒绝。
- 清理后由另一只读命令独立复核 81 个精确路径全部不存在；保留 5 份 root 私有证据文件。测试 journal 的清理不代表产品清理入口会删除永久历史；RPC 本身不删除捕获元数据/原回执/审计。
- 私有证据集：`purge-evidence-33064f5475c64236`。baseline SHA256 `DA437749944C1386577653D2177E4137ABCFCCC55D54A1B87934CAC01E5A25E2`；captures `C62157B07D7A47D14D6D776F2A0E0803DD667DFFA72F4512422FBEF85B83D440`；archives-purged `61524FF82AFA601903B5713D746F5BAA95DA8985DAE37A1DCCB73AF231D2C4F0`；rpc-result `23DCB9C52590D6A63A41A3D856F5ED9949DA4E1CB62143DEA6B174B204FF8295`；verified `F9EC5D4AFAA5614FEB1DD0B4A41B866ABD71B171746E84EC554C6229A099D512`。

## 测试与后续

- 本批 Release 构建零警告/错误；新增观察器冻结包 `D68F72E37E4B5A1C358F48EF576E913258BDA16C3F44916A97525F40B4231C6F` 两端一致，10 项实际 Linux-root 检查全通过（0.017 秒），包括硬链接与嵌套外来对象严格拒绝。检查点与输出限制有独立源码契约检查，不将它们冒充实际安装验收。
- 当前 Release 再次执行 1856 Unit / 146 Integration 全通过，本机 6 项原生依赖测试明确跳过；CI 对应原生 PostgreSQL 作业 success，但该作业不包含尚未实现的清理数据库模型或迁移。
- 4bb1b73 的 CI `39481946262` 四路 success；运行源码 56101c8 的 `39486648857` 和验证源码 6c60ea3 的 `39489338136` 后端/安装器/原生 PostgreSQL success，记录时前端仍在原作业运行，尚不报两批 CI 全绿。
- GLM 5.3 Flash 仅只读定位既有探针；独立协作代理只写临时 I/O 测试。GPT 复核并修补观察器的硬链接/嵌套外来对象拒绝、负责实际安装和执行边界。
- **未完成**：清理的 Authentik 当前管理员复核与短时确认、永久双任务/Outbox/共享捕获及层级锁、事务审计/数据库墓碑、管理员 Unknown 处置与 WebUI；安装态中断/异盘边界、孤儿与长期元数据管理、容器文件，以及完整 76 项继续。该测试中的受信验证 actor 不等于真人管理员验收，不能直接开放页面执行。
