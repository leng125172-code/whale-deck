# 递归所有者：独立签名 RPC、SDK 与安装候选

> 后续2026-11-02：宿主22906f5/1.50已正常安装，真实签名四RPC/HDD、16拒绝边界及两类旧协议兼容/独立核验通过，见[安装态证据](CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)。下文2d3b7e0候选原文保留；永久业务/API/Vue和真人页面仍待，不把安装态协议子门禁算作完整功能。

2026-11-02。候选冻结`2d3b7e0fa529d61c97d5855248e10e87eaad3c2a`，协议候选1.50。**尚未安装、未通过真实签名UDS/HDD或人类页面验收**。运行仍`0dede08`/1.49，Agent PID1851523、MaintenanceHost PID1432309/source不变；没有发布、服务重启或自动回滚。前批[底层真实容器/HDD证据](CONTAINER_RECURSIVE_OWNERSHIP_ENGINE.md)保留，不把它记为本批RPC实机结果。

## 独立协议、显式UID/GID与完整请求签名

新增`ContainerRecursiveOwnershipService`四个独立非流式方法Plan/Apply/Inspect/Discard，对应Application强类型DTO/校验、Infrastructure SDK及DI、Agent策略/Runner与固定transport。没有新增HTTP/Vue入口、永久业务实体/EF migration或Worker注册。

- 请求及capability使用独立`whaledeck-container-recursive-ownership-*`域，方法全名、protobuf descriptor及完整序列化字节进入签名。绑定原CID/实例/路径、显式目标UID/GID与原公开版本、根九字段/父身份和版本、执行UID/GID、manifestDigest/计数、operation/owner/Actor/Trace/Job/幂等键。服务从实际请求重算hash，拒绝只改body保留原header；通用及旧单对象/递归chmod authority不能授权新方法，nonce单次消费及60秒上限保留，SDK签发45秒凭据。
- Wire命令UID/GID使用`optional uint32`且必须同时HasUid/HasGid。显式0有效，省略字段不是root默认值，uint.MaxValue拒绝。Agent输入及SDK计划响应都验证presence，签名也区分“显式0”和“缺字段”。Python命令继续严格整数/范围/精确字段；不接受mode字段或caller host path。
- SDK仅接受major1/minor≥50并含`docker.files.recursive-ownership-intents`，否则发送新计划前拒绝。旧capabilities保留，普通递归权限仍使用minor49、原域和方法；协议minor广告的现有测试期望同步前移，旧Agent拒绝测试不放宽。
- 清单和逐项旧属主历史留在root私有HDD，RPC只传密封摘要与有界计数，不传整树条目或文件内容。回执Completed/Partial/Aborted/Unknown保留五类互斥计数与outcomeDigest，不能用零计数抹去Unknown或把Partial说成全树成功。历史验证事实、当前HDD可读和当前对象绑定分别判断。
- SDK只把准确的私有回执`CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND`映射null；实例消失、HDD/socket/注册表故障不能作“没有执行”的证明。错误码有限映射，未知文本统一Unavailable，不回传原异常或stderr。

## 固定最小授权及依赖安装

新增`container-file-recursive-ownership`与`-stop`固定分支，argv只含四种动作/规范32位ID，内容通过有界私有stdin。独立`/var/lib/whaledeck-container-recursive-ownership` root:root0700及两份root:root0750模块；安装/只读校验已登记，拒绝symlink和模块硬链接。transport固定名单包含旧单对象ownership、共享递归框架及新ownership引擎和必要依赖，不允许任意模块。

新受限unit只增加本操作所需的CAP_CHOWN，capability集合为DAC_OVERRIDE/DAC_READ_SEARCH/FOWNER/CHOWN/SYS_PTRACE；**原递归chmod unit仍无CHOWN**。保持ProtectSystem=strict、ProtectHome/PrivateDevices/NoNewPrivileges、AF_UNIX、禁命名空间、绑定Agent、无自动重启和control-group停止。仅可写独立状态目录、已管理apps及HDD历史路径；无SYS_ADMIN、Docker socket或shell。开发user NOPASSWD授权未扩大到产品Agent，也不自动撤销。

预算仍为300秒内核、310秒transport、320秒Runner、330秒RPC、340秒unit，10秒启动输入、5秒停止；CPU50%、384MiB、8 task、128 FD、单文件16MiB、单次输出64KiB。四动作Ready后关闭stdin，不能等待额外body。取消/异常收尾仅用原driver ID和收到的准确InvocationID停止固定unit，拒绝其他实例，不按前缀杀进程。

依赖沿用已有.NET10、protobuf/gRPC包、Linux FD/fchown、Python标准库、HDD attestation及单对象所有者；无新增包、通用命令或本地密码。新引擎所需共享模块在安装脚本中先安装。CI增加新transport/engine/候选验收器语法编译；root测试自动发现新增文件。本批安装脚本仅语法和源码检查，**未执行生产安装或验证实际systemd capability**。

## 实际执行的候选检查

目标/实施状态、RPC契约、权限与DTO边界已更新。项目图谱初次连接失败，重试恢复后按原eventId顺序补发前批engine和本批rpc-sdk，均收到成功回执并移入synced，阶段保持active；没有重复真实操作或把候选记成安装完成。

- 冻结后Release构建：0 warning、0 error。
- 完整后端Unit：**3904通过、0跳过**，其中106项递归所有者协议/SDK专项及4项新增EOF检查。覆盖完整内容签名/单次/过期/错方法/篡改/跨域、UID/GID presence、目标替换、计划字段/计数/九字段、四终态/历史观察、固定argv/精确stop、旧版本或缺cap在发送前拒绝，以及仅精确NotFound映射null。
- 完整Integration：**308通过、7项默认原生跳过**。没有新增schema；这不等于本功能真实PostgreSQL或UDS验收。
- `test_container_file*.py` Linux-root：**434通过、0跳过**。本批13项新transport/profile及旧依赖回归；包含真实临时inode fchown/实际SIGTERM后Partial不续做、实例消失只观察、缺回执/通用依赖不可冒充absence、异常argv在systemd前拒绝，以及旧chmod无CHOWN。HDD attestation/容器会话在这些单测中明确合成，非生产HDD或已装helper。
- 12项host runtime artifact检查通过；它们使用合成artifact元数据，不是新自包含发布物实测。Python严格语法、shell语法、diff、8项范围检查通过；209份MD的1267个相对链接零缺失。
- 只读运行核验：当前11核心healthy，ID/镜像/状态/StartedAt与上一已验收引擎批次baseline一致；Agent/MaintenanceHost原PID及实际来源0dede08不变，其他文件意图/保护/Job/终端/锁预检均0。不把旧健康结果代替本轮读取。

生成脚本曾在factory命名自检处停止，已保存的代码保留并修正；没有真实资源操作。新测试首次编译缺少AgentGrpcService的DockerEngine参数（CS7036），修正静态capabilities用例为既有null依赖方式后通过。初次3900 Unit之后补4项EOF并冻结，最终3904通过；失败和修复不记成一次首轮全绿。

```sh
dotnet build WhaleDeck.slnx --configuration Release --no-restore
dotnet test tests/WhaleDeck.UnitTests/WhaleDeck.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/WhaleDeck.IntegrationTests/WhaleDeck.IntegrationTests.csproj --configuration Release --no-restore
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_container_file*.py'
python3 -m unittest discover -s tests -p test_host_runtime.py
```

没有新真实登录、UDS/安装态helper、HDD场景或PNG；前端源码未变，没有借旧页面截图或测试作新证据。下一步冻结宿主覆盖与真实签名四RPC/HDD/拒绝/兼容和准确清理，再永久EF业务/共享保护/实时身份/Worker/API/Vue及真人页面/故障矩阵。ACL、可恢复删除、长期清理、P0-05回退来源及其余59项继续；17项移出不计完成，master未交付。
