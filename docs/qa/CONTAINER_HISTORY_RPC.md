# 容器 HDD 历史：独立只读 Agent 协议候选

2026-10-27。本记录描述候选源码；安装及真实 RPC 的结论必须在部署后另行记录。

Agent minor42 新增独立 `ContainerHistoryService.List/ReadSource`。请求体摘要、方法和短时一次性签名使用独立域，上传签名及普通能力不能调用；API/Worker SDK 先协商 major/minor 和两个能力。当前容器/原始启动实例字符串/路径、捕获摘要与当前目标证明逐层校验。历史原实例可早于实际重启后的当前实例，不能借此放宽当前容器范围。

Root 固定 helper 仅有 list/source 枚举，使用受限 systemd profile、精确 InvocationID 和两个只读槽；无命令、任意路径、恢复或发布入口。安装阶段准备固定私有 HDD 历史域，GET 不创建或收养目录。历史 listing 的 Present 仅表示外形和大小可读；来源读取会在发送前后检查完整内容、元数据及 FD 身份。最终 end 直到校验上下文退出、helper EOF/正常退出才发出，SDK 也必须读取到 RPC EOF 才能正常完成，不能把收到最后一个数据块当成功。

本批结果：Release 零警告/错误，2666 Unit 全部通过；191 Integration 通过，7 个专用原生夹具默认跳过，不能视为本批真实 PostgreSQL/Valkey 验收。230 项 Linux-root 容器文件测试通过，22 项备份安装测试通过。新增 SDK 测试使用合成 UDS 服务，覆盖断流、尾帧、摘要、范围和签名，并非实际 Root 证据。新 Root 传输测试覆盖最后一块之后校验失败不发 end，以及 BrokenPipe。首次构建遇到覆盖参数命名分析器、测试同步委托重载问题，修正后完整重跑；失败日志仍在 `/tmp/wd-container-history-rpc-build*.log`。

实际安装门禁为 `validate_container_history_agent.py` 与冻结 `TerminalProbe container-history`：通过真实管理员 BFF 上传生成旧版本，独立 SQL/Root/HDD 校验后，真实签名 UDS 完整读取 2MiB+7；实际仅重启独占普通容器，再从旧捕获读回，验证错误 MAC/重放/请求体变化/核心容器拒绝。另执行实际文本编辑网页门禁；它们尚未运行，不以候选测试代替。

尚缺永久历史目录数据库、当前权限/审计的恢复计划和确认、来源到暂存/发布的完整恢复任务以及 API/UI。只读内部来源不是文件恢复功能，不签 P2 文件管理整项或总目标完成。

## 首次安装门禁失败与只读沙箱修复候选

7b38c0f 已完成正常覆盖发布并验证六组件/HTTP来源；真实 BFF 成功捕获旧版本后，首次 List 在真实 `ProtectSystem=strict` 下返回 `FILES_BACKUP_READ_ONLY`。共用 HDD 校验器把只读读取也误当成写入；没有放开沙箱写权限。失败证据 `container-history-agent-87572c4567ce4b4f9f43134f55944d01` 与日志 `/tmp/wd-container-history-agent-7b38c0f.log` 保留，自有数据准确验证清理，永久 SQL/Root/HDD 不删除。

修正为可信内部显式只读 target/attest，禁止只读模式创建 namespace；挂载、UUID、HDD 和文件身份校验保持，默认写入遇只读仍拒绝。新增完整来源测试也验证错误 UUID 依然拒绝。231 项容器 Linux-root、22 备份安装测试及 Release 零警告错误通过；首轮新测试的 fixture 属性引用错误已修正并完整重跑，日志保留。修正尚待再次覆盖及真实 RPC 完整重跑。

CI 安装器失败另由进度元数据漏写“17项移出不算完成”触发，已补回范围说明，8项范围检查通过；未删除或削弱检查，完整安装器本机重跑和新 CI 继续。

## e6b5034 修复后真实安装门禁通过

正常 dev 覆盖 `whaledeck-dev-e6b5034.service`，0/SUCCESS，3分27.168秒。当前 Agent/MaintenanceHost/API/Worker/Gateway/Migrator 与实际 HTTP 来源均为 `e6b50346a6de585fd3c0664d0a253323a0f32e34`，Agent minor42。宿主校验通过；安装的历史引擎、共用备份模块和固定 helper 全SHA与该源码匹配。沙箱保持 ProtectSystem=strict，HDD只读；未扩大 ReadWritePaths。

独立冻结客户端来自 `terminal-probe.LJ1Can`。真实管理员 BFF 上传及 Worker 成功生成物理 HDD 旧捕获；直接签名 UDS List/ReadSource 首次读取完整2097159字节，随后实际停止/启动仅本次独占普通容器，以新当前实例读取同一旧实例捕获，再完整核对字节/摘要。缺 MAC、上传域 MAC、请求体变化、重复 nonce 及核心容器均拒绝。完整 RPC EOF 后才签成功，当前目标、被保留旧 inode 和 HDD 原件在两次读取前后均不变。

私有证据 `deploy/artifacts/container-history-agent-c18138aedb04452b87bbfa54142319b6/`、日志 `/tmp/wd-container-history-agent-e6b5034.log`。准确验证后删除测试容器/源目录，永久 SQL/Root/HDD 历史保留。另在同一 e6b5034 再跑真实文本编辑：两次桌面/手机完整编辑、取消零内容、旧inode/HDD、正常双端跨页退出全部通过，证据 `container-text-browser-a137843b4a814127a549460685b4a33d`；没有用前版的网页结果替代本版。

独立最终 SQL：31条永久上传意图（16 Succeeded、8 Canceled、7 Failed），活动/Unknown/租约/发布屏障均0；临时上传验收身份0，四轮源目录已缺失、四容器均明确 no-such-object，11核心healthy。额外只读清理核对起初严格匹配 Docker 英文大小写/空白失败，读取实际退出码1、空白stdout及明确 no-such-object 后重新准确检查；未为核对失败删除任何其他对象。

原安装器完整36组866测试本机正常结束，另新增只读修复231容器/22备份安装通过；e6b5034 CI已确认后端/原生PG/原生Valkey/安装器success，前端完整浏览器组仍运行，尚未宣称全CI通过。完整恢复 SQL目录、来源绑定持久意图/二次确认、授权调度、发布旧目标HDD留存和API/UI仍待，当前只验收内部来源，不签P2整项。
