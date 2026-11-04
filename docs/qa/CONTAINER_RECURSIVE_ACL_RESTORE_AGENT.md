# 递归 ACL 历史恢复：1.55 安装态签名 RPC 分项验收

日期：2026-11-05。**Agent/MaintenanceHost 已标准安装为 e066ec1 / 协议 1.55，常规签名恢复及迟到不重放分项通过。永久业务、共享保护、Worker/API/Vue、真实 Authentik 恢复页面与完整故障矩阵尚未完成。** 不增加 59 项主任务的完整完成数。

## 来源、标准安装与维护入口

- 产品源码：`e066ec1dfa158576186b49450610a7a65cbe2fd0`，准确 CI `41869390182` 为 completed/success。
- 原生探针：`ae79e86c7f12491f8c9f000d58d9fef490bd8f61`，复用已冻结、已核对 SHA 的自包含产物；不是另改旧探针或重放旧 operation。
- 安装态验收脚本：`55df39e464c747e605c73c0701a1b49fb785353c`。本批新增固定采集、独立 Root/HDD/child history/outcome/transcript 证明，原始字节归档；新增 13 项专项与旧 156 项关联共 **169 Linux-root 通过、0 跳过**，严格 Python 编译及 diff 通过。测试使用显式临时容器/HDD夹具，不与下方实际安装证据混用。

在独立 detached e066ec1 工作树运行标准 `deploy/host/publish.sh` 和 `install.sh`；工作树只增加公共 SDK digest 引导行，不复制私有 `.env`，SDK 容器只接收 `git archive` 公共源码。Agent/MaintenanceHost 的自包含 .NETCore、AspNetCore 和 Pkcs 均为 10.0.13；完整 SHA256SUMS、实际 apphost `--build-info` 与标准 `validate.sh` 通过。安装创建独立 restore Root 0700 目录，安装两个 root:root/0750 模块及原有受限 helper；user NOPASSWD 按授权保留。

| 实际过程 | InvocationID | 终态 |
|---|---|---|
| 标准宿主发布 | `e84ff163c0374cda9538a0f77479e4b6` | ExecMainCode=1 / ExecMainStatus=0 / success |
| 标准宿主安装 | `42f89e43eea44f55a8980687ee1b6b66` | ExecMainCode=1 / ExecMainStatus=0 / success |
| 签名恢复整轮 | `a05d1f8d8681400aa206a959efb4521f` | ExecMainCode=1 / ExecMainStatus=0 / success |

准确 receipt 与 journal 均在停止 QA unit 前保存。最终 Agent PID **1080518**、MaintenanceHost PID **1080514**，来源均 e066ec1。API/Worker/Gateway 未重新发布，11 核心容器的 ID/镜像/启动信息保持，并显式读取确认全部 healthy；这不是四镜像覆盖发布。

**维护连续性仍未通过：** 正确 `/maintenance/status` 入口的 67 个实测样本中，4 个未取得 HTTP200，完整样本保留。安装后入口恢复可达不能抹去中断，也不据有限采样推断准确中断时长。本批未自动回滚 dev，没有重启主机、Worker、修改网络或防火墙。

## 全新独占资源的实际操作

使用新 no-network、非 root/dropALL/read-only-root 容器及唯一可写 bind，四个对象含中文路径，每个文件 **2 MiB+7** 字节，初始 ACL 含命名 UID0/权限0、基础 access、异构 default。首次捕获前，独立进程已经重开 ContainerRoot/全部 FD，确认 namespace/非 IDMAP、完整字节、ACL/普通属性、九字段身份及旧 Root/HDD 保全。

先通过旧普通递归 ACL 的签名 RPC 生成真实来源；源码、来源元数据和原始 frame 均分别绑定，不能拿历史验收的来源冒充新捕获。

| 分项 | 实际结果 |
|---|---|
| 三次新普通来源捕获 | access、default、后续新的 access 变更；全部签名完成，HDD 原前像与各次 before 树一致 |
| `access-complete` | 按来源逐路径恢复异构 access，default/字节/所有者/mtime/普通属性保持；基础 access 的无扩展 xattr 也实查 |
| `access-noop` | **新 operation**，Completed、changeCount=0，仍保存完整四项当前前像和四个 child Root/HDD；全树九字段保持 |
| `default-complete` | 恢复来源目录的原 default；文件保持 noChange，access 和非目标元数据保持 |
| `discard` | 当前计划确实有变更，写前取消后 Aborted；notStarted=4，其余四分类/变更计数零，无 child 或聚合 HDD，迟到调用不写入 |
| 每个新恢复的观察/重放/取消 | 每项原初调用后执行 Inspect、迟到 Apply、Discard；原回执字节与 HDD 原件、计数和 outcome 保持，物理树不重做 |
| 后续新变更后的迟到旧 Apply | 先实际执行另一笔普通 access 变更；旧恢复仍 Completed、旧 counts/digests 保留、replayed=true/historyAvailable=true，**bindingCurrent=false**；新树及全部旧 Root/HDD 字节保持 |
| 拒绝矩阵 | **22 项 Plan / 23 项 operation**，共45项实际拒绝，含独立签名域/方法/篡改/nonce/来源/版本/上下文/frame presence、核心与来源作用域；不是前批仅预置的候选计数 |

数量口径：**3 个新普通来源、4 个独立恢复 operation（3 Completed / 1 Aborted）、17 个恢复 receipt、24 份 child HDD、6 份新聚合 HDD。** 17 个 receipt 包含同 ID 的观察/迟到调用，不能称为17次新恢复。拒绝后的私有 NotFound 检查可创建空锁，不能据此声称没有任何锁文件。

## 独立证明与保全

三个独立读取阶段分别为：首次捕获前实际读盘；清理前再次重开实际容器/FD/HDD；清理后只重验保留原件、完整转录与精确源/CID 缺席。最后阶段明确 `sourceBytesRereadAfterCleanup=false`，不声称重新读取已删除字节。

独立校验包含 source/restore 两套 aggregate、child ID/history/outcome 域；每项操作/owner/manifest、完整定位与父拓扑、原 ACL、实际 after 九字段和版本、子回执确认及不变的原始归档字节。来源与当前树的计数分开；改变项的 ctime 采用真实 after，不错误要求回到原 ctime。source-rpc 与 restore-rpc 使用独立日志目录，转录分别8行和24行，缺失、错序、伪造当前绑定及自洽重算摘要均有离线负测。

| 保全对象 | 实际前后结果 |
|---|---|
| 标准保护文件 | 安装前1404份字节保持；安装允许重写的四份配置元数据单列，其余九字段保持；验收后普通来源 Root/HDD 新增30份，共 **1434** |
| 新 restore Root | 安装后空目录；验收后 **32 文件：16 JSON+16锁**，全部保留 |
| restore HDD 独立快照 | 原28文件保持，新增18文件，最终 **46文件：37 JSON+9锁**；该 namespace 不在旧标准快照内，已单独保全 |
| 目录 Root | **94** 文件保持 |
| 永久递归 ACL 业务 SQL | **38 intents / 35 executions / 73 jobs** 保持，活动工作与 source/restore transient helper 均清零 |
| 精确清理 | 自有源目录与准确 CID 移除，Root/HDD 历史保留；没有递归清理产品目录或重复真实旧操作 |

源与配置 HDD 均为 device **2049**；只证明实际配置备份通道，**不签收 NVMe/HDD 两块物理盘分离或异盘灾难恢复**。没有创建人类身份、真实 BFF 审计或 Vue 页面证据。

私有证据：`recursive-acl-restore-install-c2ed40a27c514ef083ccff14a02bbb42` 的16份与 `container-recursive-acl-restore-agent-57a1ad8ec068402ea71a357acb4ed8dc` 的82份，共 **98 份 SHA 封存核对通过**；另有标准宿主产物/探针各自的 SHA 清单。

## 工具修正和剩余工作

首次安装前预检把 Docker 命令返回的 bytes 与字符串名称比较，触发断言；实际11容器均 healthy，发布与安装尚未启动。修正显式 UTF-8 解码并补两项失败关闭测试后，169项通过，再启动唯一的发布/安装/实机整轮。未在已完成操作上重试、改绑定或补签成功。前序原生探针的两个状态顺序修正及内核整轮失败仍保留各自原记录。

下一步实现独立永久恢复意图/EF 迁移、共享写入保护、Worker、管理员 API/Vue 与真实 Authentik → 页面 → SQL/Worker → Agent → 文件/HDD 整链；再做来源丢失/替换、撤权/退出、重启/取消/Partial/Unknown 等完整矩阵，推进可恢复删除、维护连续性和其余59项。当前常规安装态恢复通过，不等于完整权限、文件管理或整阶段完成。
