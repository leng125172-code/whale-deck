# 目录覆盖恢复 Agent 候选（RPC1.34，2026-10-20）

最新安装态验收：验证源码57877a9对已装6a6f8e2完成专用签名UDS/固定helper及2MiB+7、1GiB+7原件/旧目标/新输出/两组归档manifest完整核验，新夹具精确清理及独立健康核对通过。首轮观察器把回收新私有目录误判为必须保留旧目录inode，已按真实语义修正并补5项Linux root回归，原文件inode与全部元数据/完整内容仍严格保留；失败现场未删。源/备份同HDD，不能当作物理异盘证明。新BFF/Worker/真实网页、Overwrite捕获复用及中断交换仍未签收，[详细证据](FILE_TREE_OVERWRITE_RESTORING_UI.md)。下方未执行/未实现为前序历史。

2026-10-20后续：覆盖恢复SQL/第10类现任管理员对账/Worker/API候选d50b765已通过工作站隔离真实PG1执行1通过，1738 Unit/146 Integration（6原生本机明确跳过），尚未部署。新增专用签名UDS客户端草案 `TerminalProbe tree-overwrite-restore`：严格自有fixture前缀、2MiB+7与1GiB+7、历史捕获与覆盖前备份不同owner、双树/目标版本、防重放/错方法/指纹/取消/幂等；只通过RPC操作，不创建或清理磁盘夹具，输出明确要求独立观察器。已接Program及有界错误输出，最终Probe Release构建0警告/0错误；该草案尚未在工作站执行，不能作为安装态验收。独立原件/归档/输出全字节观察器与自有载荷精确清理仍待实现，Overwrite捕获再次恢复也明确未覆盖。以下为前序历史。

最新安装态补记：为修复首页磁盘采集，6a6f8e2的Agent/MaintenanceHost已实际安装并通过宿主在位验证，因此1.34不再是“未安装”候选。前序7d2bd38及6a6f8e2四CI已全部成功；新覆盖恢复专用签名UDS/独立磁盘证明仍未执行，业务任务/Worker/API/UI仍缺，不能把宿主健康检查当作覆盖恢复功能验收。用户随后通知离线，远程步骤等恢复通知。下面未安装/旧运行时描述保留为实施历史。

## 范围与边界

在96ae47b内部引擎之上新增独立 `FileTreeOverwriteRestoringService`：Plan/Apply/Inspect/Discard、`files.tree-overwrite-restore-intents` 和 RPC minor34。旧不存在目标恢复保持独立方法/契约/授权，不把 overwrite 参数塞进旧 RPC。

固定helper仅接受四个完整 `tree-overwrite-restore-*` 枚举命令和受限invocation ID；不接受脚本、HDD路径、UID/GID或成员清单。驱动只加载六个root所有、单链接、不可组/其他写、大小有界的普通模块。沿用UDS peer限制、60秒以内/方法绑定/单次nonce签名，不增加Agent TCP端口。

**尚未部署或实测安装态签名UDS，没有业务API/Worker、永久任务/捕获登记或WebUI。** 当前实际仍18f38bb/Agent1.33，不能宣称覆盖恢复已对用户可用。本批无数据库schema变更。

## 契约与安装

- Command分别绑定历史source版本/capture资源事实、当前target版本；capture owner不替代当前actor。
- Plan包含完整输入归档、目标清单/全树摘要、目标和父目录UInt64 identity、两根策略与独立fingerprint。目录16GiB/10000项、归档17GiB边界保持，不退回普通文件1GiB上限。
- Receipt区分原捕获与当前操作产生的覆盖前捕获，新增后者bytes/SHA。未核验时只允许空ID/摘要和零证明值。Completed必须有备份、原件保留和交换证明；Unknown不制造成功version，Aborted不宣称交换。
- 新增专用 `files-tree-overwrite-restore` 默认授权。安装器仅在整个配置精确等于旧1.33或更早已知默认时升级；保留root/GID/当前权限及独立0600原始备份。自定义、收紧、部分根已加授权均不自动扩大。
- 安装/在位校验增加新模块；helper沿用有界目录任务资源预算，覆盖前归档允许17GiB输出。不执行自动恢复、重试交换或回滚。

## 已执行验证

- 新契约专项76/76通过：严格JSON字段、UInt64完整范围、独立目标版本、证明/flag矛盾、Runner前拒绝。另4个capability拦截器用例验证错服务/错动作、无Token、重放均不触发continuation。
- 统一Release build零警告/错误；1463 Unit、143 Integration通过，本机6项原生依赖测试明确跳过。本批尚未冻结实库验收，不把跳过当真实PG通过。
- 首次合并build遇到测试CA1861；将重复数组改为static readonly直接遍历后重新全量构建/测试通过，没有关闭分析器或降低门禁。
- 实际Linux root：27项覆盖恢复/可信loader/固定driver通过（59.342s），含前序24项、六模块逐一元数据异常、四动作分流/幂等/追加输入/未批准动作拒绝及归档bytes/SHA回执。HDD身份为明确合成fixture，读写、inode、fsync、交换和SIGKILL为实际Linux；不是安装态RPC/独立物理HDD证明。
- 实际Linux root：23项默认配置升级测试通过（0.036s），严格默认/原始备份及权限/幂等/自定义保持/冲突拒绝/匹配备份复用。没有更改工作站实际注册表。
- `bash -n`对新helper/install/validate通过，不冒充真实systemd调用。
- 改动后的相邻旧引擎72项实际Linux通过：文件传输30（0.217s）、不存在目标恢复19（92.013s）、回收23（107.978s）。持续观察原运行句柄至exit0，没有因大文件耗时重新启动测试。
- 前序96ae47b的CI39383074744四作业已全部completed/success；本RPC候选CI尚未签收，不借前序CI证明新源码。

三个GPT协作代理分别补契约/capability测试、驱动/配置测试及只读范围盘点。主代理复核并负责协议/生产实现、统一构建、Linux验证。共享proto、权限、策略和工作站写操作未分散执行。

GLM Flash只读定位Agent注册点；主代理纠正其将FileTransferRunner/共享验证器误判为“项目外”的定位错误。一次成功/零重试，111072ms，input283823/cached240128/output2603，费用未返回。未以外援结论替代源码。

## 后续

冻结CI与实依赖、安装态签名UDS/独立磁盘证明，再接Application永久双任务、路径/捕获锁、Worker/取消/Unknown处置、捕获索引、API/页面和真实身份验收。目录清理、容器文件及全部76项继续，P2-03/P2-04仍部分实现。
