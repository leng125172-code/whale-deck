# 开发空间归档签名 RPC 与隔离实盘验证

2026-11-10，接续 [固定 helper](DEVELOPMENT_ARCHIVE_HELPER.md)。前提交 `01206806df79b21bc376cc6257f34723c3eb79cb` 的准确 [CI 42813645016](https://github.com/leng125172-code/whale-deck/actions/runs/42813645016) 已完整 **12/12 成功**。本轮新增 Agent 1.77 内部归档传输候选；未接入持久业务、Worker、BFF 或 Vue，不增加59项及全部子门槛的完成数。

## 已实现

专用 `development.archive.tasks` 能力及 Start/Observe/Cancel 三个短 RPC。每次签名绑定完整 protobuf、RPC 方法、45秒过期和一次性 nonce；正文包括任务/归档/空间/存储/环境/停机 epoch/阶段/计划与回执摘要及当前 actor/trace。重复 header、旧的仅方法签名、重放、跨方法与正文变更均拒绝，没有通用弱签名回退。Gateway 同时要求协议 major=1、minor≥77及精确能力。

固定 helper 的输入另外按 Python 的完整 canonical JSON 重算 `requestSha256`。host 的稳定请求不含可能更换的管理员上下文，当前管理员仍须由未来业务重新授权；protobuf 签名继续绑定当次 actor/trace。Gateway 仅接收已分配、Stopped且原 epoch 匹配的空间，不接受任意宿主路径、命令或 unit。没有公开 HTTP 接口或 Worker 派发器。

成功回执限制为固定、有界的归档事实，不传文件清单、内容、日志或任意路径。Agent 与 Gateway 两端都校验字段闭集、类型、唯一属性、状态、原请求绑定、存储/epoch、数量/字节上限；重新计算 plan 和 capture receipt 摘要，Verify 必须使用原请求中的 capture 摘要。`sourceVersion` 保持宿主的大写hex，其余摘要为小写。JSON null不能冒充空字符串，布尔/小数不能冒充整数。

Start、Observe、Cancel 使用独立并发预算。Start/Cancel 已入场后不因 RPC 断线终止控制事务；独立复制单元继续归宿主持久 fence 管理。通信异常不自动重试、不生成新 task，也不改成成功或已停止。Pending 不证明尚未执行，Finishing 继续观察；Interrupted 禁止同任务重放，Stopped 不证明部分包不存在，更不授权删源码、释放配额或预算。

## 已验证

- Release 全解决方案构建通过，**0 warning / 0 error**；全量单测 **6249/6249**、新增归档协议/RPC **12/12**，零跳过。签名测试使用真实 Unix Socket、生产拦截器与 Gateway，helper receiver 明确为 double；Python canonical 向量为合成协议夹具，不能代替实盘。
- Python 归档、操作、运输及 QA 收尾专项最终 **55/55**，零跳过。覆盖预启动取消、派发不确定不重发、原执行/日志绑定、实际 Landlock/seccomp，以及 stdin 关闭异常仍回收桥进程、退出不确定时保留私有目录。
- 最终实盘 `development-archives-native-b248b59eece74d27a538c609d3c11bc9` 使用生产 C# `DevelopmentArchiveGrpcService`、身份 peer guard、签名拦截器、真实固定 helper、PID 1/systemd、Landlock/seccomp和物理 NVMe/HDD 上各2 GiB的独立 ext4镜像。没有存储/执行 double。Plan→Capture→独立 Verify 三个长任务成功，回执一致；复制中 Observe/Cancel、预启动取消、重复 Start、晚到取消均通过。
- 原生调用同时实际拒绝无签名、旧签名、重复 nonce、跨方法、重复 header与 actor正文篡改；临时 key 只在 root私有 `/run` 树中使用，退出清零并移除，没有保存进证据。挂载替换仅发生在子进程的私有 mount namespace；生产 helper inode/内容、配置、源码/配额保持。原生 .NET进程、各原执行、镜像、挂载和私有资源均已收尾。
- 既有直接捕获、重命名前后真实强杀、重新挂载、无源码独立重读和真实内核容量/项目配额拒绝同步回归通过。正式300GB HDD仅做只读核验，没有创建产品归档。200MiB复制取消夹具为稳定观察使用10% CPU/90秒，产品 helper 仍为100%/7200秒。

原生 QA 是手动授权的可信仓库验证程序，不是已安装的提权接口。RPC 探针只能用 `WHALEDECK_ARCHIVE_RPC_PROBE=1` 开启，执行同目录固定项目的 Release publication；拒绝任意路径选项，禁止符号链接，复制前后核对二进制摘要并收紧复制件权限。挂载 namespace 只隔离挂载视图，不声称能限制任意 root程序集。

## 已部署：前序固定 helper 版本

准确 CI通过后，干净部署仓库从42ea297d快进至01206806，经固定 `whaledeck-platform-maintenance update` 和专用oneshot执行标准发布，没有回滚。单元最终 active/exited、MainPID=0、Result=success、ExecMainStatus=0；部署账本 current=01206806、previous=42ea297d、pending=null。这只部署前序 helper，不包含本轮1.77 RPC候选。

独立读取真实二进制/镜像证明六组件均来自01206806，Agent/MaintenanceHost实际进程exe inode与安装文件匹配；23个开发/公共宿主模块与该Git tree字节一致，标准 `validate.sh`通过。11核心容器健康，其中8个数据库依赖容器身份/镜像/启动时间不变。5空间的原物理源码、inode、quota和生命周期与发布前相同；9772条既有开发/任务记录内容摘要保持，活动任务和路径锁为0，私有配置、120GB/300GB存储布局与授权user NOPASSWD保持。另按上一实机批次已封存的after快照独立复核8401条原审计和14份原报告内容摘要保持；不是伪造本轮即时before快照。最终两个入口的主页和维护状态共4项HTTP检查均200。

部署中只取得部分观察窗口：两端口共428次采样，402次200，另各13次HTTPError，覆盖约6.3秒的采样起止跨度。初版未记录HTTPError的具体status code，不能据此签具体503根因、精确中断持续时间或全程监听连续性；当前恢复健康不追认该门槛。维护连续性仍单独保留待处理。初版六组件核验误把额外的`components.http`观察标记当成第七个组件而拒绝；现精确核对6个组件键和独立http标记，原失败保留，没有放宽来源校验。

## 失败及未完成边界

首轮原生探针因复制 DLL 保留组可写权限被安全检查拒绝；已收紧复制件为root私有权限再重验，原失败及镜像/挂载收尾记录保留。初版数字拒绝单测的字符串替换未命中，修正测试输入后实际拒绝小数；没有放宽解析。早期两轮通过记录亦独立保留，不替代最终固定来源探针。部署观察脚本初版换行转义错误原件保留，修正后仅记录其实际覆盖窗口，不签全程维护连续性。即时快照的审计表筛选曾误写表名，9772条不含审计；另用上一批原始封存审计/报告快照独立核验，原查询不回写、不补造before。

汇总证据位于 `deploy/artifacts/development-archive-rpc-20261110-01206806/`。四轮原生记录共406份原件均独立封存，包含首轮失败；最终128份原件的清单SHA256为 `7aa7cba7e372caead50b1312f60c4009c18591cea968f140ad301f6dbbd97274`。原未提交 source 合并预览 diff SHA256 持续为 `e56a10d30f5cb49bf771ae6fc9bd35a3564bfc586b4ca3338899940b84c0eca2`，保持未提交。

本轮 RPC 仍需新提交的准确 CI、标准 dev发布及安装态 RPC复验。随后实现持久归档意图、当前资源授权、共享事务围栏和明确确认；验证摘要必须来自服务端原任务，不能信任浏览器或 HDD自报摘要。载荷外防复活、精确退役、内核 quota/claim归零、数据库预算最后释放和完整真实身份用户闭环尚未完成。全部59项、ST272、PET6和16子门槛继续，17项移出不恢复。项目图谱待同步。
