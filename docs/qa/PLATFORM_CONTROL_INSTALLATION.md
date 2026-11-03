# P0-05维护工具安装与可信发布历史：134fd25实际验收

2026-11-03。**维护工具已受控交接并完成一次正常dev覆盖；六组件当前均134fd253bae0c343850e0057f4b18f8f7d55ddeb/协议1.51。实际权威账本current=134fd25、previous=306f646，旧错误previous原件保留为legacy。** Agent PID253100、MaintenanceHost PID253095，11核心healthy、8数据库平台容器未重建，配置/能力密钥/开发及产品sudo摘要保持。本批不自动回滚、不重启主机、不执行真实手动版本恢复，也不签完整P0-05或59项完成。

## 受控安装交接：先保留实际旧基线

进入时实际六组件均306f646，Agent4135740/维护4135726；运行checkout也为306f646。生产尚无新revisions记录器、独立recovery runner、boot单元或权威账本。原previous-revision错误记录目标306f646，未将其手工改成447070e或凭历史文档伪造成功基线。

从干净已提交134fd25冻结七份固定来源至root私有目录，先检查shell/Python语法及字节；获取原root:root/0600全局发布锁并证明fd9，检查11核心身份/健康、两宿主PID/InvocationID、SQL活动/租约/屏障/终端均空闲、私密配置/两份sudo规则/能力密钥摘要。新六组件实际来源探测一致后才开始交接。旧固定工具原始字节单独保留，原生产记录不变。

用候选的原子安装逻辑先安装独立恢复runner、service/timer/tmpfiles，再安装revisions记录器、maintenance shell、privileged wrapper；逐文件确认root:root/0750、单链接、非链接及冻结字节一致。恢复单元和配置root:root/0644。tmpfiles、daemon-reload、enable boot及enable --now timer完成；实际新单元在发布锁持有时无写入跳过，解锁后无pending时执行也成功。两阶段均未创建权威账本或改旧previous。

交接后实际11核心完整ID/镜像ID/StartedAt/healthy、两宿主PID/InvocationID及配置/能力/sudo摘要与进入时完全相等，维护HTTP200/healthy，SQL空闲。实际host validate.sh通过运行库、固定HDD策略、UDS/权限/受限helper/Docker及动态发现的有效入口；没有通过完整安装器提前替换C#宿主二进制。

## 唯一正常覆盖与真实发布历史

正常Git拉取首次发生GnuTLS握手中断；第一次随后的ff检查只是Already up to date，未部署。再次正常fetch成功后精确快进到已推送134fd25；未修改TLS校验、远端或全局Git配置。开发用户index属主保持1000。

只启动一次`whaledeck-dev-publish-134fd25.service`，Invocation为`9e1f5d033b5f4538843bcb7a76711663`，MainPID249364。12:55:03至12:59:31 CST，约268秒；真实终态active/exited、MainPID0、Result=success、ExecMainStatus=0，随后只停止这份已经结束的自有unit并核对inactive/dead/success。完整journal与前后状态保留，未把进行中的Result=success误作完成。

实际新维护shell在锁下执行原样prepare，来源取六组件实际程序集/镜像及宿主运行exe/DLL映射，**初始化current=306f646，previous=null，pending=update/134fd25及原token**；没有拿已快进的checkout HEAD充当基线。旧previous原字节保存为previous-revision.legacy，再生成root私有三工具检查点。发布期间账本和索引为prepared，回执不存在，没有提前晋升。

完整host publish/install与受限依赖检查、冻结镜像构建、正常Migrator及API/Worker/Gateway替换均由这一次标准update执行。宿主和容器全部就绪后，保存的可信记录工具再次验证实际六组件来源，finish晋升为：

| 字段 | 实际值 |
|---|---|
| currentRevision | 134fd253bae0c343850e0057f4b18f8f7d55ddeb |
| previousRevision | 306f646110229496959c0c8a2e8f5a2f1570a0e7 |
| pending | null |
| lastCompleted | 原update目标、原token，未替换成新尝试 |
| previous-revision | 正确投影306f646，来源为权威账本 |
| previous-revision.legacy | 与进入时错误旧记录SHA完全一致 |
| 控制入口receipt | completed、controlsRestored=false、applicationReplayed=false、automaticRollback=false |
| 活动control-recovery.json | 已移除；三份快照/回执仍root私有保留 |

这证明正常升级的最后成功/真正上一版记账与完成收敛已实际修复，不证明应用手动恢复、同版重发或中途断电都验收通过。

## 安装态与独立收尾结果

- 本轮重新执行47项相关测试：47 tests / OK；不是前后端全量复跑。发布源码134fd25对应CI41580351197本轮观察时backend/native-postgres/native-valkey/installer四路success，frontend仍in_progress，未宣称整条CI通过。
- 独立收尾再次核对实际API/Worker/Gateway镜像与CLI、两宿主CLI及实际PID/exe/已映射DLL inode、无网络/只读Migrator元数据、API/Gateway HTTP来源全部为134fd25。元数据探测不运行数据库迁移；标准update本身的正常迁移步骤不与其混淆。
- 实际三应用容器ID按覆盖发布更新；8数据库平台完整容器ID/镜像ID/StartedAt/healthy与交接前完全保持，11核心最终healthy。SQL活动任务、终端、主机锁及七类意图/租约/屏障均0。
- 完整安装后再次host validate.sh通过：运行库/PKCS 10.0.13、固定HDD策略、systemd/UDS/限制入口及当前活动IPv4维护/身份入口均通过。私密.env/.images.env、能力密钥、开发及Agent sudo规则SHA/UID/GID/mode保持，user NOPASSWD:ALL仍可用，未修改密码。
- 实际新恢复服务在真实发布持锁且prepared时启动，返回success/0、NoNewPrivileges=yes、ProtectSystem=strict、仅cap_chown/cap_dac_override；原账本/索引SHA不变，未生成恢复回执。真实锁忙边界通过；完成后自有发布unit的ExecStopPost无pending只读接续，已完成账本不变。
- 实际whaledeck-agent不能读取root账本或恢复源码，且不能sudo任意Python调用恢复；产品sudo动作未扩大。此证据不等同所有人类/API权限用例重新执行。
- 实际已安装MaintenanceHost二进制另起临时loopback/私有status进程，验证缺状态通过固定健康探测、代理绕过、损坏/超长拒绝、未知字段不泄露和no-store后准确结束/清理；生产status未修改。

## 保留的观察器失败与证据界限

外层启动脚本漏了systemd-run的--no-block，Type=oneshot启动等待超过其30秒命令超时；**发布unit已经启动并继续运行，并非产品发布超时或失败**。该脚本停止了自己的HTTP观察器，原日志和30份初始采样保留。后续尝试重启已自动卸载的transient观察unit返回exit5；没有停止或重发产品发布，而以另一份自有临时观察unit接回同一Invocation。

最终两段共216样本均HTTP200、updating→healthy、错误0；中间观察空窗明确保留，不能据此声称完整268秒无中断。修正了观察方式，未更改产品超时、数据库、时钟、账本或成功门禁。收尾脚本只有严格确认同一Invocation真正成功后才验收并停止已结束的自有unit。

本批没有新Authentik用户会话或实际容器文件/HDD写入页面验收。先前306f646的真实权限UI/HDD证据仍按原批次保留，不能把它们改标134fd25。此处的实际HDD检查是安装策略/依赖/挂载验证；没有新历史归档内容链，也不把源与HDD同device当跨盘证明。

原件位于root私有忽略目录`deploy/artifacts/platform-controls-handoff-134fd25/`：40份静态文件SHA清单（冻结7工具/探测器、旧工具、交接/实际账本/Root回执摘要、同Invocation完整journal、真实六组件/进程映射/11核心/空闲与两段观察、失败脚本/原日志）；后续Agent拒绝和安装态隔离进程两份原件另有followup SHA，原40份再次校验一致。目录0700，原件全部root:root/0600，凭据正文未入原件、Git或日志。

## 未验收范围与后续

P0-05仍部分实现：真实应用手动版本恢复、发布期间进程强杀后的安装态接续、物理冷启动/掉电、挂载/空间/混合来源及标准bootstrap失败恢复尚未签收；合成账本/影子systemd的前批[候选证明](PLATFORM_CONTROL_RECOVERY.md)不能补签这些范围。按目标约束不主动重启主机/断电、不自动回滚dev；后续先补安装态受控故障与真实身份页面，再推进ACL其余矩阵、递归/历史恢复、可恢复删除和长期清理及其他59项。

当前59项主表和整阶段完成率不调整，17项移出不算完成，旧25%–35%粗估不因一个分项上调；UI/C#酒馆/统一供应商/AI Live2D范围保留。以下历史候选的“尚未安装”按各自批次理解，不覆盖本批实际事实。

文档复核：8项范围测试通过，8份变动MD的787条相对链接有效，目标第1节与两份旧候选QA原正文逐字保持；git diff --check通过。文档登记和部署分项不提升59主项完成率。
