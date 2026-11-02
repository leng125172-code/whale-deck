# 容器递归普通权限：Agent 1.49 安装与真实签名 RPC

> 后续2026-11-02：宿主已覆盖e668dbe/1.49，递归及单对象遗留回执保护、三类共16操作兼容与递归实际回执丢失/独立核验通过，见[后续候选与修复](CONTAINER_RECURSIVE_PERMISSIONS_WORKFLOW.md)。以下95c0787批次保持原始证据，内部业务仍未接EF/Worker/API/Vue。

2026-11-02。宿主 Agent/MaintenanceHost 已正常覆盖为冻结 `95c07870e005fa758a23356c2d8347f88b1c05ac`，协议1.49；API/Worker/Gateway仍为 `d1fcca8`，数据库平台没有重建或迁移。递归引擎及安装态签名协议子门禁通过，**永久业务意图/共享保护/Worker/API/Vue和真实身份页面仍未接通，递归完整功能未验收**。59项继续，17项移出不算完成；未自动回滚dev。

## 冻结发布与实际运行来源

通过既有 publish-and-install.sh 宿主流程，用固定摘要 SDK 镜像从95c0787的git archive构建自包含Release Agent和MaintenanceHost，源码构建容器不挂载工作区/private配置，产物SHA256SUMS校验及.NET10.0.13/PKCS10.0.13门禁通过。只安装宿主；未重建或重启API/Worker/Gateway、数据库容器，没有新生产EF迁移。

安装前预检任务/终端/宿主锁/上传与目录屏障，以及复制/移动/权限/所有者未结束意图或屏障全部0；保存11核心容器ID/镜像/状态/StartedAt和两个宿主旧PID及真实执行来源。更新unit为whaledeck-recursive-host-95c0787，启动回执InvocationID为eff8ef64a4ea46ffa0d3f3ecf4c9759c。发布日志正常结束、实际新进程来源与安装校验通过，原journal记录Deactivated successfully，Result/退出状态为success/0；unit清理后时间戳为空，不据此编造安装耗时。

安装器按既有流程正常重启Agent和MaintenanceHost，两PID均预期改变。另从准确新/proc/PID/exe确认真正apphost及sourceRevision=95c0787，不用磁盘文件替代运行进程证明。**RPC验收期间两PID保持不变**，不把安装期间重启误称为维护PID不变。宿主validate.sh通过实际运行库、私有权限/单链接、UDS、受限helper、HDD证明、Docker及动态入口检查；11核心容器跨安装与RPC全部ID/镜像/状态/StartedAt不变，最后全部healthy。

原开发user NOPASSWD:ALL仍保留，单文件visudo及sudo -n true通过，未自动撤销或改密码。产品递归helper仍没有CHOWN/SYS_ADMIN或任意shell能力，沿用固定argv、白名单根、HDD及准确Invocation停止约束，见[候选协议说明](CONTAINER_RECURSIVE_PERMISSIONS_RPC.md)。

## 实际签名 UDS / 固定 helper

探针同样从冻结95c0787的Git源码，用固定SDK镜像发布到root私有目录并校验SHA。读取capability key仅在私有文件/进程内，结束清零；没有把token、密钥、原始异常或私有环境写入Git/日志。通过真实/run UDS、已装C#服务、sudo固定helper和受限临时systemd unit，不调用内部Python引擎直接代替RPC。

唯一自有无网络/只读根/非root/限资源容器，合法HDD bind中新建四项固定树（2目录、2文件）。每文件2097159字节，普通user属性有实际值；源属主2100:3100与容器内核执行FSUID/FSGID1000:1000分别核对。没有真实人类登录，也未新增永久业务意图、Job或网页。

| 原操作 | 实际结果 | 物理验证 |
| --- | --- | --- |
| 文件0600、目录0700 | Completed，4完成/2变更 | 目标模式、原inode/属主/内容/纳秒mtime/用户属性符合 |
| 相同模式的新操作 | Completed，4完成/0变更 | 全树九字段含ctime保持 |
| 文件0640、目录0750 | Completed，4完成/4变更 | 原inode/属主/全部字节/mtime/属性保持，全部目标模式符合；不是恢复原混合模式 |
| 执行前明确Discard | Aborted，0变更/4未开始 | 全树九字段不变；无HDD清单/子项状态；迟到Apply仍Aborted |

每个操作由行帧探针与独立Python进程交互：预检之后、执行之后、每次Inspect/迟到Apply/Discard之后，父进程完整读回文件、用户属性和整树九字段，再核对原Root JSON逐字节不变。新操作可以改变模式，原操作的重放不能重做chmod；相同模式不能借一次多余chmod改变ctime。每批固定派生4个子项ID，原Root和HDD留存，不用新operation接续旧执行。

实际拒绝边界为8个预检检查（无签名、错域、过期、目录模式body篡改、特殊文件模式、核心容器、只读根、nonce重放），以及3个执行前检查（manifest body篡改、错误方法授权、准确私有回执不存在）。拒绝后独立确认原树不变且无执行JSON，之后才合法执行原绑定；目标消失/HDD错误不能据此泛化为空结果。不是模拟鉴权或伪造Root返回。

3份实际整树HDD元数据、12份已执行子项HDD历史和4份永久Root均验证。独立第二进程从安装态Root原件/HDD重读，与父归档逐字节比较，重算完整计划/根版本/清单/历史/结果摘要、原ID和逐项时间线，验证预期2/0/4变更与取消无历史，准确源/CID已清理，临时unit0，原预检项全0、11核心healthy。它只写独立验收报告，不改权限、不创建HDD历史，源清理后没有再读取物理字节的声称。

## 旧协议兼容与自动检查

同一新Agent1.49和冻结探针还运行旧单对象普通权限6个真实签名操作：文件修改/无变化/恢复、非递归目录修改/恢复、执行前取消，共5完成1中止。另一个只读进程核对6份Root/5份物理HDD、原inode时间链、目录孩子不变、准确资源清理与核心不变。不是重复旧operation，也没有把旧所有者人类页面证据改记为本批回归。

本批新增探针Release编译零警告/错误，Python -W error语法、8项范围/12项宿主artifact门禁、实际host validate与Git diff/link检查通过。产品代码沿用aead344候选，其3678 Unit（93新专项）/258 Integration（7默认原生跳过）/395 Linux-root是前批已运行结果；本批只新增QA，不冒称再次执行全量或当前CI全绿。931fd3c的远端CI查询为后端/原生PG/原生Valkey/安装四路成功、前端仍运行，此为查询时事实，不是新95c0787提交的终态。

本次安装态递归RPC、独立验收和旧权限兼容均首次通过，没有将本批失败重写成通过。前批损坏历史、SIGTERM/候选SIGKILL等记录保留在原QA，不把内部测试切点移算为本批安装态故障。

## 证据与接续

私有忽略目录：

- deploy/artifacts/recursive-host-95c0787/：before/after/final、publish.log及原unit journal；记录安装前后进程、实际来源和核心不变。
- deploy/artifacts/terminal-probe.I9FbGM/：冻结自包含探针及SHA256SUMS。
- deploy/artifacts/container-recursive-permission-agent-b3e167f5a8e94793bf3117b6dd5f6693/：baseline、逐RPC回执、4操作前后整树、Root/子项归档、3份HDD聚合归档、physical-final、verified及independent-final；实际HDD子项原件保留。
- deploy/artifacts/container-permission-agent-f93d8bb9267647e89c3d9db9cdb9095c/：旧协议6操作Root/HDD/准确清理证据。

日志/tmp/wd-recursive-installed-probe-{build,publish}.log、wd-recursive-host-95c0787-validate.log、wd-recursive-installed-rpc-first.log、wd-recursive-installed-independent-first.log、wd-recursive-old-permission-{regression,independent}.log；均不入Git。

继续接永久业务意图/EF迁移、确认与当前实时Authentik授权、跨操作共享屏障/路径/预算、专用Worker、API/Vue、Partial/Unknown处置及明暗小屏真实身份→页面→实际树/HDD全链。安装态进行中SIGTERM/硬停止/Worker或Agent重启、租约/撤权/坏元数据和实例变化等完整故障矩阵、真实大树/设备/挂载/NVMe来源及长期清理仍待；不能用本批四个正常协议操作代替。递归所有者、ACL、可恢复删除与其余59项/UI/C#酒馆/统一供应商/AI Live2D继续，master尚未正式交付。
