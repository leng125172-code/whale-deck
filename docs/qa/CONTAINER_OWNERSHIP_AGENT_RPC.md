# 容器所有者变更：独立引擎与Agent1.48实施恢复点

2026-10-30。独立非递归UID/GID引擎、专用签名RPC、SDK和固定受限helper已实现，宿主Agent/MaintenanceHost已安装`2a9ceb6`/协议1.48。**仅引擎和安装态协议子门禁通过；永久业务意图/EF迁移、共享写入屏障、Worker、管理员API、Vue和真实人类页面尚未接通，不能签所有者功能完成。** API/Worker/Gateway仍为`cb4c400`。递归、ACL和删除继续；59项active，17项移出不算完成。

## 本次实现与授权边界

- 独立`ContainerOwnershipService.Plan/Apply/Inspect/Discard`、`docker.files.ownership-intents`、完整protobuf请求体/消息类型/方法绑定的一次性HMAC。SDK要求主版本1、minor≥48与独立能力；只有旧chmod能力时拒绝发送所有者请求。普通权限协议、消息和签名域未放宽。
- UID/GID为两个必须显式提供的可选存在位uint32；明确0有效，遗漏与4294967295 sentinel拒绝。Root JSON还拒绝布尔、字符串、浮点、负值和额外字段；不允许借此传入mode、递归、ACL或宿主路径。
- 三种身份分开：Plan顶层OwnerUid/Gid观察容器进程的内核映射FSUID/FSGID；SourceIdentity为实际inode原属主；Command.Uid/Gid是管理员将来明确确认的目标。不能用Docker Config.User字符串猜测内核身份，也不能把执行身份当作原文件属主。
- 固定helper单独增加CAP_CHOWN，限既有容器根/白名单路径和原inode/版本/父目录绑定，不扩大旧chmod的capability集合。Root原文件必须普通模式、无硬链接/符号链接/特殊对象、无ACL或security xattr；这些情况明确拒绝，等待对应高级功能，不默默清除权限。
- HDD使用独立`container-ownership-v1`和`ContainerOwnershipMetadata`，先留存原UID/GID/模式/指纹与属性摘要再单次fchown；不是文件内容备份。保留原inode、普通mode、内容和纳秒mtime，不递归处理目录子项。
- 独立永久Root回执包含ownerConfirmed true/false/null及HDD可能存在/已验证/摘要；原ID重复Apply只观察，不再次fchown。明确取消先创建永久Aborted；缺回执、目标变化/消失、实际属性漂移保留Unknown，不能自动恢复或接纳外来对象。
- fsync在真实fchown之后失败时，通过原inode和HDD观察实际完成，避免错误报告“未修改”；外部替换和容器消失保持未知。公开业务入口未来必须另接原登录授权、永久任务、七方共享保护及审计，不能直接把可信SDK作为管理员API。

## 内部真实磁盘与进程切点

冻结`532ceb9`：非root、无网络临时容器挂载实际HDD来源和实际NVMe命名卷，两个设备ID不同且NVMe sysfs明确匹配。文件和目录变更/原属主恢复共20操作，含8次专属候选子进程真实SIGKILL（Prepared及OwnerChanged）。16 Completed/4 Aborted；2MiB+7文件全量、原inode、普通模式/mtime、混合子项及20份实际HDD元数据通过。第二个独立只读进程重算计划/历史摘要、原指纹时间链并核对准确源/CID/卷清理，11核心不变。

这些强杀使用候选子进程检查点，不是安装产品故障或人类业务/UI证据。该批没有调用生产Agent新RPC。

首轮`62adfb2`验证器全局字符串替换误将夹具Path.chmod写成Path.fchown，初始化AttributeError，尚无容器/内容/备份；原失败保留。唯一新建空目录仍为umask077/0700，核对确切名称、root属主、空内容、inode/创建时刻与私有证据目录一致后准确删除，没有扩大清理范围。修正初始化并先保存scope后完整重验通过。

## 宿主安装与来源

- 单元`whaledeck-ownership-host-2a9ceb6.service`，InvocationID `963ed229f2aa4e9fb3dc2e54a820b3df`；同一次发布2分15.023秒成功结束，journal明确Deactivated successfully。未因等待重开、未回滚。
- Agent/MaintenanceHost实际build-info为2a9ceb6；运行MainPID的exe device/inode、托管DLL映射inode与安装文件一致。受限helper与新状态目录安装权限通过；运行库、UDS、systemd和动态入口门禁通过。
- 11核心容器ID/镜像/状态/StartedAt全部与发布前一致，没有构建或重建API/Worker/Gateway、database-platform。应用仍cb4c400，不把混合来源声称为六组件同版本。
- user NOPASSWD:ALL保持，单文件visudo及sudo -n true通过；正式Agent仍仅走固定helper，新的CHOWN只在所有者分支。API/Worker无新增端口或Docker Socket。

## 已安装签名UDS与固定helper

冻结2a9ceb6自包含TerminalProbe，全部产物SHA清单校验后调用真实已装UDS。

- 文件1000:1000→2200:3200、同属主无变化、恢复1000:1000；目录2100:3100→2200:3200→原属主；另一次先Discard再迟到Apply永久Aborted。共6原操作，5 Completed/1 Aborted，5份物理HDD元数据与6份Root原件核验通过。
- 完整2097159字节文件及同内容子文件一致；原inode、普通mode、纳秒mtime保持，目录子项原2200:3200未变。无变化操作的完整九字段指纹不变；原属主恢复后匹配最初指纹前八项。
- 无签名、旧通用签名、篡改UID或完整计划、签名重放、漏UID、核心容器、只读容器路径均拒绝；读取与原引用重放不会重复变更。密钥只在内存/私有文件中，探针结束清零，不写Git或日志。
- 原inode完整链、各计划和历史独立域SHA、原Root/HDD原件与准确测试源/CID清理由另一只读进程再次核对通过；活动transient为0，11核心不变。删除后没有再次读取物理源的声称。
- 旧普通权限6操作签名RPC使用同一新探针/已装1.48回归通过，另一个只读进程确认原inode链/Root/HDD与准确清理；旧业务协议继续可用。

安装态本轮RPC来源是HDD；上述NVMe是独立内部引擎证明，不冒称安装态NVMe业务或完整跨盘UI已验收。本批没有真实人类BFF Cookie、永久业务Job/SQL或所有者页面证据。

## 自动检查及接续

- 3478 Unit/213 Integration通过，7项默认原生依赖跳过；103新增所有者签名/策略/SDK专项包含在全量中。首次全量的7个旧minor47固定断言、随后尚未更新的6个断言保留失败日志，改为实际minor48后全量通过，原能力和拒绝断言保留。
- 24新增所有者内核边界、9新传输测试及全部362容器文件Linux-root执行通过、零跳过；类型/ID/JSON边界、五处真实SIGKILL、fsync实际已改归属、容量、ACL、安全属性、原inode、只观察与取消等有检查。Release/探针编译零警告错误，Python -W error、shell语法、5安装CLI/12宿主运行库/8范围检查通过。前端未改，本轮不复用旧浏览器结果当新所有者UI通过。
- 早期测试夹具把当前Linux-root测试进程误认为声明的Docker User=1000；调整为观测该固定PID的实际FS身份，真实Docker/NVMe和安装态另行严格要求1000:1000。没有改产品为适配夹具。首次RPC测试中的旧Mode字段/缺using及SDK能力名问题在候选生成前已修正；未在生产关闭门禁。
- 私有证据：`deploy/artifacts/container-ownership-bfa9429454484523a4d4322ded31b825/`（20操作/8强杀）；`ownership-host-2a9ceb6/`（安装前后/进程来源/原journal）；`container-ownership-agent-87be4d1d11b9433ba2ece5b59938e3d7/`（真实UDS/6 Root/5 HDD/独立核验）；旧权限回归`container-permission-agent-41f9ddf722cc4d76ad555c0e7bbc8472/`。上述证据不入Git。日志为`/tmp/wd-ownership-*`，失败与通过分开保存。GitHub CI尚未取得全成功终态，不签全绿。

接下来实现所有者永久意图/三层业务绑定、EF migration、双Job/Outbox/取消/租约/恢复、七方双向共享写入保护、专用Worker/管理员API、Vue原UID/GID预检确认/接续/Unknown处置，最后用真实身份、实际文件与HDD和页面故障整链验收。之后继续递归、ACL、可恢复删除等59项；这些功能尚未完成。
