# 开发空间归档候选安装与真实运行回归

2026-11-10，42ea297d44667c733b5f81a7164e8e8cacb221aa 已通过准确 CI、标准 dev 覆盖发布和真实身份回归。内部 HDD 归档引擎已安装，归档的产品授权、RPC/Worker/BFF/Vue、空间退役及配额释放仍未实现，不能增加完整主任务完成数，也不交付 master。

## CI 与标准部署

- [CI 42801371681](https://github.com/leng125172-code/whale-deck/actions/runs/42801371681) 的目标 SHA 精确匹配，12 个任务全部成功，包括完整六分片浏览器回归及聚合检查。状态观察曾发生 EOF，失败记录保留，重新取得准确成功回执后才发布。
- 实际部署仓库从 ddc832df 干净快进至 42ea297d，使用固定 maintenance update 入口与 oneshot 发布单元；终态为 active/exited、PID 0、Result success、ExecMainStatus 0，没有自动回滚。
- 六组件来源一致；实际 Agent/MaintenanceHost 进程的 exe inode 与安装二进制一致。新 archives 模块、storage 重构及相关固定 helper 与目标 Git tree 字节匹配，宿主标准 validate.sh 通过。
- 11 个核心容器最终健康，原 8 个数据库容器身份不变。私有配置及已授权 user NOPASSWD 保持。NVMe 120000000000 字节和 HDD 300000000000 字节完全预留镜像、内核项目配额强制状态前后相同；未创建生产归档。

## 真实身份与独立保护核验

真实 Authentik 临时管理员经 Vue/BFF、Worker、签名 Agent 和安装态宿主工具，完成新空间 8cfab552-f080-439b-8f97-ccb152563dc1 的 8 GiB 配额分配、源码上传和页面确认发布。Node 测试 2/2、.NET 构建、独立 .NET 测试 2/2 通过，测试无跳过；源码实际断言 ProcessorCount 为 1。JSON/Markdown 报告下载摘要与真实测试产物一致，构建成功没有代替测试成功。

新空间实际停用/恢复为 epoch 1→2→3，恢复后 Node 测试 2/2 和报告下载通过。两个原空间从实际 epoch 5→6→7 完成另外四次切换，另两个原空间保持 epoch 3；共六次成功启停。停用入场拒绝、原请求只读重放、普通账号越权、CSRF/Origin 拒绝及双端退出通过，两临时账号已删除。

独立于浏览器状态重新核对原 4 空间的物理源码树、inode、内核配额及数据库摘要：原 14 个运行/Job、14 份完整报告、8355 条审计和 42 条原任务事件内容保持。加上本批 4 个新运行，18 个运行均终态、ReportComplete、无 lease；本批原执行 bundle/test-report 与 cgroup 已收尾，私有诊断只含有界码数并绑定原请求。新空间保留等待受支持的产品退役，不进行 SQL 删除或手工删目录。

## 未通过与保留问题

维护入口观察的 148 次采样有一次 URLError，时间紧邻安装流程重启 MaintenanceHost；原探针没有记录底层 errno，不能推定具体连接错误或中断时长。当前 8080/8081 均为 HTTP 200/healthy，仍不能签全程连续可用。这是既有 [P0-05 监听连续性缺口](CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)，本批失败、精确服务替换日志和当前健康分别保留。后续需隔离验证监听交接/连接排空，并分别记录双端口、单调起止及有界错误码，不能降低零失败门槛。

实际查看 320 像素暗色截图：启停卡和成功报告可见，但环境/存储描述的右值出现中文逐字竖排。固定运行时的多个 dd 共用一个 dt，可能使后续值落入标签列；须修复布局并真实重验，不签完整移动端可读性。

初次补采任务事件时，QA 查询误以为存在独立 id，实际主键为 job_id/sequence；失败 SQL 原件保留，改为实际复合键后捕获并复核 42 条内容摘要。一次 root 只读 Git 状态刷新改变了部署仓库索引元数据，已仅对该索引恢复 user:user/0664，后续 root Git 使用 GIT_OPTIONAL_LOCKS=0；工作树未改。原未提交源码合并预览始终保持原 diff 摘要。

## 证据与接续

私有部署/独立核验目录：deploy/artifacts/development-workspace-archives-delivery-42ea297d-20261110/。真实身份/UI/报告原件：deploy/artifacts/development-installed-runtime-f4a35ae2035b4f2db4b1a1b9fcd746ad/。原 [隔离实盘候选](DEVELOPMENT_WORKSPACE_ARCHIVES_CANDIDATE.md) 的六份封存清单不回改。

两个新目录分别封存 56、32 份原件，SHA256-MANIFEST.json 摘要分别为 2b83e35744507e269be326dbfff96b020d7fa49a540e12c76138ecdf47a20fc1、1d70454660723f230bcff16455f73d3daf94fd96712c59b5b78c7c9c1e88a79e。独立只读复核重新核过实际来源、SQL 原行及报告；原六份候选清单与新 QA 共 140 个文件内容仍匹配。首轮封存检查拒绝了历史独立脚本按默认 umask 新写的 9 个 0644 证据文件，所在目录始终 root 0700；仅将本批这些原件元数据收紧为 root 0600 后封存，正文未改，失败和修正记录保留。

继续按现有 workspace 事务入场围栏实现停用空间的持久归档意图、明确确认、签名传输、可恢复任务和独立完整验证；将授权任务中的回执摘要作为信任锚，再进入载荷外防复活标记、精确资源移除、内核 quota/claim 归零及预算释放。捕获有 16 GiB/10000 条目等实际能力上限，损坏 partial 保留，不自动另建任务重拷。全部 59 项、ST272、PET6、16 子门槛继续，17 项移出任务不恢复；图谱待同步。
