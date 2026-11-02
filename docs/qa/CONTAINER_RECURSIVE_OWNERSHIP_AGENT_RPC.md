# 递归所有者：Agent 1.50 已安装与真实签名 RPC/HDD

> 2026-11-02后续：内部业务编排候选7a94733已实现并验证独立请求/计划/执行/确认HMAC、当前身份、原登录、观察恢复与明确处置；尚无具体EF仓储/迁移、共享保护、Worker/API/Vue或真实业务页面，本批没有重跑下方已完成的实机操作。[候选与边界](CONTAINER_RECURSIVE_OWNERSHIP_WORKFLOW.md)。下方安装记录及原始失败保留。

2026-11-02。宿主Agent/MaintenanceHost已正常覆盖冻结`22906f51c117fa4f4d0ec6c7c8586fbc63cf0b6d`，协议1.50。递归所有者安装态签名协议子门禁通过；**尚无本功能永久业务/共享写入保护/Worker/API/Vue或真人页面证据，完整递归所有者未验收**。API/Worker/Gateway仍`0dede08`，数据库平台及生产schema不升级。59项继续，17项移出不算完成；没有自动回滚dev。

## 冻结构建、标准安装与运行来源

源树干净且冻结SHA/验收器/探针及已装模块逐字对账。标准`deploy/host/publish.sh`使用digest固定SDK镜像和Git archive构建自包含Release Agent/MaintenanceHost，不挂载工作区/private配置；host制品SHA256SUMS和真实.NET10.0.13/PKCS10.0.13运行库门禁通过。探针同样从该冻结Git源码发布，私有SHA清单通过。宿主与探针制品先构建完，再重新预检并运行标准`deploy/host/install.sh <host-published>`；没有重复已完成的宿主构建。

安装前全局Job/终端/宿主锁、上传/目录屏障及复制/移动/普通权限/所有者/递归意图或保护均0，保存11核心ID/镜像/状态/StartedAt和两个旧宿主实际来源。安装unit为`whaledeck-recursive-ownership-install-22906f5`，InvocationID=`ca4a3096f3184853addb2c65979a6c41`；实际Result=success、ExecMainStatus=0，安装结束并通过validate后才停止本次已完成unit，回执保留。HDD attestation在替换前执行，未绕过实际挂载证明。

| 实际运行进程 | 安装前 | 安装后及RPC结束 |
| --- | --- | --- |
| Agent MainPID / sourceRevision | 1851523 / 0dede08 | 2113282 / 22906f5 |
| MaintenanceHost MainPID / sourceRevision | 1432309 / 0dede08 | 2113274 / 22906f5 |

以真正`/proc/PID/exe` apphost及`--build-info`确认运行来源，不以磁盘文件代替。标准安装正常重启两宿主，**不把安装窗口称为维护PID不变或零中断**；RPC与兼容验收期间两PID保持。实际host validate通过运行库、UDS/私有目录、模块root:root0750单链接及独立状态0700、新固定helper、HDD证明和动态维护/8080/8081入口。user NOPASSWD授权文件单独visudo与实际`sudo -u user sudo -n true`通过，保留原授权，不改密码。

11核心跨安装和全部RPC的ID/镜像/状态/StartedAt与安装前相同，最终全部healthy；8数据库平台不重建。API/Worker/Gateway实际镜像revision均为完整`0dede0804a5f9d1847c23744b90c7e3bae5f7751`。只有宿主覆盖，没有本功能生产迁移或持久业务任务。

## 真实签名 UDS、受限 helper 与实盘树

私有自包含探针通过真实UDS→已装C#服务→sudo固定helper→受限临时unit，密钥仅从私有文件进入进程内存并在结束清零，没有token/密钥/原始异常或private env进入Git/日志。Actor是专用私有协议探针上下文，**不是正常人类Authentik/BFF登录**；不调用内部Python变更引擎替代RPC。

独占准确自有、无网络、非root1000:1000、只读容器根、限资源、唯一合法HDD bind的容器；固定树2目录+2文件，每文件完整2097159字节、混合原UID/GID/普通模式、普通user属性及纳秒mtime。容器真实FSUID/GID1000:1000与原属主、请求目标分开校验。13份已装helper/模块逐字匹配冻结源码，按原4项清单执行。

| 真实原操作 | 实际结果 | 物理结果 |
| --- | --- | --- |
| 目标2200:3200 | Completed，4完成/4修改 | 原inode/完整字节/mode/mtime/user属性保持，仅目标属主及必要ctime变化 |
| 同目标新操作 | Completed，4完成/0修改 | 全树九字段含ctime不变，不多做fchown |
| 显式0:0 | Completed，4完成/4修改 | 明确root UID/GID生效，原mode/内容/mtime/属性保持；不是省略字段默认root |
| 执行前Discard，目标2300:3300 | Aborted，0修改/4未开始 | 全树九字段不变，无HDD清单/子项状态，迟到Apply仍Aborted |

每次Plan之后、执行之后、每次Inspect/迟到Apply/Discard之后，父进程完整读取所有文件字节、普通属性和九字段，核Root原JSON逐字不变。原operation/owner/清单保持，重放不重复fchown。源与HDD device均2049，只计同设备元数据留存，不计NVMe业务/异盘灾备或HDD故障。

实际预检拒绝**11项**：无签名、旧递归权限域、过期、GID body篡改、UID/GID各自uint.MaxValue、UID/GID各自缺失、核心容器、只读根、nonce重放。执行前拒绝**5项**：manifest/UID/GID body各自篡改、错误方法授权、准确私有回执不存在。拒绝后独立确认原树与原执行JSON不变，再合法执行；不能把HDD/容器故障泛化为空结果。

3份真实聚合HDD、12份子项旧属主HDD及4份永久Root对账。独立只读第二进程重读安装态Root/HDD原件与私有归档，重算完整计划/根版本/清单/历史/子计划/版本/结果摘要，核ownerConfirmed严格true、全部目标和3次完成的4/0/4修改及取消无历史。源清理前完整字节由父进程读取；第二进程明确不声称清理后重读已删除源字节。准确源/CID清理、全部预检0、残留临时RPC unit0、11核心healthy。

## 旧协议实际兼容与失败记录

同一新Agent/冻结探针另用新资源运行旧递归chmod四操作（3完成/1执行前取消），8预检/3执行拒绝、3聚合/12子项实际HDD、原inode/UID/GID/完整字节/mtime/属性及目标模式和重放保持；独立核验通过。默认chmod的旧摘要域/namespace/无CHOWN profile未扩大。

旧单对象所有者另6个真实签名操作（5完成/1中止）、6份Root/5份实际HDD和原inode时间线/孩子保持，独立核验通过。旧核验器首次误传目录名而非所需绝对路径，前置路径检查拒绝；改为绝对路径后只重跑只读核验，**没有重做6次真实操作**。这不记为独立核验首轮全绿。

构建收尾的两个失败均保留：首次systemd环境缺精确仓库safe.directory，在Git读取前失败，未构建或安装；只给构建进程设置本仓库信任后，宿主构建成功，直接执行未带执行位的探针脚本被拒绝。随后通过bash单独构建探针，复用已验SHA的宿主制品。原脚本/journal/失败状态保留；只在归档并验证准确InvocationID、失败且MainPID/ControlPID为0之后，reset-failed本批两条失败构建unit，没有清除其他服务失败。

宿主实际安装、新所有者RPC及两类实际兼容操作均完成通过；不将上述构建/只读参数失败删除或改写成从未失败，也不借前批内部SIGKILL证据充本批安装态进行中故障。

## 自动检查、CI与证据

本批新增探针Release编译零警告错误、5项范围/序列化门禁、12项host artifact门禁、8项任务范围检查、Python严格语法和diff通过；210份MD的1274个相对链接零缺失。项目图谱本批安装态event已收到成功回执并移入synced，阶段仍active。前批3904 Unit/308 Integration（7默认原生跳过）/434 Linux-root仍是候选记录，**本批没有冒称重跑这些全套**。

冻结22906f5的远端CI为`41440833378`，查询时backend/native-postgres/native-valkey/installer成功，frontend仍运行；不记整条CI成功，也不将其远端合成UI算本机人类验收。本批没有新增PNG、真人页面或永久业务证据。

- `deploy/artifacts/recursive-ownership-host-22906f5/`：构建请求、两次失败原件、成功自包含host制品及SHA、探针发布记录、安装前后/final、实际运行来源、标准install/validate日志、unit Invocation及成功/失败退出状态。
- `deploy/artifacts/terminal-probe.Ntq7Gz/`：冻结自包含探针与SHA。
- `deploy/artifacts/container-recursive-ownership-agent-6473a7e972924cb2a29af02afddbb5fe/`：真实四RPC交互、4操作前后物理证明、Root/子项归档、3份HDD聚合归档、verified/independent-final。
- `deploy/artifacts/container-recursive-permission-agent-071f74d803b44513b799d029778ea71b/`：旧递归chmod4操作与独立证明。
- `deploy/artifacts/container-ownership-agent-92dff2ff12074b99b07a195bdbf806ce/`：旧单对象所有者6操作与独立证明。

均私有忽略目录、root/0700，日志与原件0600；可执行制品为受校验的root文件，不入Git。永久Root/HDD保留，没有宽泛删除或自动回滚。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_ownership_agent.py 22906f51c117fa4f4d0ec6c7c8586fbc63cf0b6d /home/user/GitRepos/Precision-7920-Tower/whale-deck/deploy/artifacts/terminal-probe.Ntq7Gz/TerminalProbe
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_ownership_agent.py container-recursive-ownership-agent-6473a7e972924cb2a29af02afddbb5fe
```

已有回执，不为展示重复第一条真实操作或安装。第二条独立只读复核；未来宿主换版/重启后应保留本批运行边界，不覆盖旧PID事实。

接着实现递归所有者永久业务/EF迁移、请求/计划/执行签名、共享写入屏障/路径/预算、实时Authentik与原登录检查、Worker/API/Vue及真人树/HDD页面；补进行中取消/撤权/重启/坏元数据等矩阵，再ACL/可恢复删除/长期清理、P0-05回退来源及其余59项/UI/C#酒馆/供应商/AI Live2D。17项移出不计完成，master未交付。
