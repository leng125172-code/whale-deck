# P0-05维护工具持久恢复：1176084/4eb4d15候选及隔离systemd证据

**后续134fd25安装与实际正常覆盖（2026-11-03）**：新工具已安装、六组件134fd25、真实current134fd25/previous306f646及旧错误legacy保持均通过；原候选“尚未安装”仅指当时状态，下文原批次正文保留。实际手动恢复/安装态强杀/物理冷启动及身份文件-HDD页面仍未验收，见[安装、真实历史与边界](PLATFORM_CONTROL_INSTALLATION.md)。

2026-11-03。**候选已实现并通过47项相关测试及原样恢复模块的隔离systemd验证，尚未安装生产工具、创建生产账本或执行新生产发布/手动恢复。** 运行仍306f646/协议1.51，Agent4135740、MaintenanceHost4135726。最终systemd探针前后11核心的完整容器ID/镜像ID/StartedAt/healthy及两服务PID/InvocationID完全相等，生产固定工具/previous原件/账本缺省状态保持。此前[真实306f646部署与旧记录问题](CONTAINER_ACL_STALE_PREFLIGHT.md)、[可信发布记录候选](PLATFORM_REVISION_HISTORY.md)保留；这不是P0-05整项签收。

## 本批修复的具体问题

此前正常EXIT trap可以恢复被旧安装器覆盖的维护工具，但SIGKILL不执行trap，重启后/run也会消失。手动恢复到旧commit可能遗留旧维护入口，无法可靠继续使用新的权威账本。1176084将恢复所需证据持久化，另设不依赖应用运行状态的独立入口；4eb4d15只补齐QA观察器对全部11核心及StartedAt的核对，生产恢复模块与1176084相同。

- 固定root入口`/usr/local/libexec/whaledeck-platform-control-recovery.py`只使用Python标准库；恢复路径不执行Git、Docker、安装器、捕获的脚本或应用命令，没有新的sudo动作、RPC/API或浏览器身份源。
- 新boot oneshot在tmpfiles和本地文件系统就绪后、Agent/MaintenanceHost前恢复；一分钟timer处理锁忙后的接续。现有固定update/rollback临时服务增加ExecStopPost，同一个恢复命令处理shell强杀。恢复锁忙只跳过，不阻塞正在发布的事务。
- 主维护shell在全局fd9锁下先完成上次检查点恢复，再prepare/保存本事务检查点，最后才运行安装器。状态通过独立恢复入口写入，finish使用进入时保存的可信记录工具，避免旧安装器覆写其运行路径。
- host installer在改变账号/固定工具前取得或验证同一把锁；其`install` CLI必须证明继承fd9。手动恢复有活动检查点时保留当前独立runner、service、timer和tmpfiles，旧应用安装器不会降级这些入口；后续更新只能保持schema1兼容。
- installer预建Agent StateDirectory的正确服务属主，再创建root私有子目录，避免首次systemd启动递归改属主；tmpfiles预建两个原有共享运行目录，不扩大到生产数据库或HDD归档路径。

## 持久状态、安全与失败恢复

固定`/var/lib/whaledeck-agent/platform-maintenance`为root:root/0700。`control-recovery.json`为当前唯一检查点索引，严格schemaVersion1/capture/operation/phase/controls；action/40位revision/32位token必须匹配权威deployment-revisions.json，旧完成token或另一个pending不能拿来恢复。它不是第二份发布历史，也不改写数据库。

先在`control-preparing-<capture>`保存三份固定工具（maintenance、revisions.py、privileged）和严格checkpoint.json，文件root:root/0600、目录0700；全量SHA256/大小校验、文件及目录fsync后重命名为`control-<capture>`，最后原子发布索引。未发布索引就中断时，恢复不会扫描或猜测残留目录。三个可信输入全部验证后才替换第一个工具；错权限/属主、软硬链接、额外成员、损坏哈希、账本不匹配均拒绝，保留原件供排查。

每个固定目标通过目录FD、同目录临时文件fsync/原子替换/目录fsync写成root:root/0750；不跟随目标符号链接。恢复自身不会执行备份字节。中途再次强杀可由新进程重新逐一核对，最终收敛到相同三文件。错误仅输出有界结构化code/message，不回显路径、用户输入、凭据或原始异常。

| 持久事实 | 恢复行为 | 应用/发布账本 |
|---|---|---|
| prepared，未完成update | 恢复进入时三份工具，报告failed | 不重做应用、不晋升或撤销账本 |
| prepared，未完成rollback | 恢复当前维护入口，报告failed | 不自动继续旧版本、不自动回退 |
| update已完成、尚未写committing | 保留新工具，报告中断 | 保留已完成账本，不把工具降级 |
| committing且完成账本匹配 | rollback恢复当前入口；update保留新入口；报告healthy | 只确认原事实，不执行部署 |
| 记录/快照不能证明 | 拒绝恢复，保留检查点/原件 | 不修补或猜测生产事实 |

恢复先写root私有receipt.json（明确applicationReplayed=false、automaticRollback=false），再原子写受限公共状态，最后移除活动索引。状态写失败保留索引以便重试；已存在回执也会重新核对全部目标。公共failed文案只说明失败或中断，不提前声称工具已恢复。完成的快照与回执保留取证，不由boot入口扫描/递归删除；强杀后的精确Git工作树也保留，恢复入口不执行Git清理。快照/残留长期清理仍需单独设计预算与验收。

## 本批实际执行结果

- 最终`python3 -Werror::ResourceWarning -m unittest discover -s tests -p 'test_platform_*.py'`：**47 tests / OK**。包含原账本/来源/状态与shell回归、新检查点/安装保留/真实锁继承、各固定文件替换前后六次真实临时子进程SIGKILL、committing后及索引发布前强杀、新shell进程组强杀后独立恢复、坏输入在首写前拒绝及状态写失败重试。普通用户入口拒绝也通过；未以默认跳过冒充运行。
- **11冻结构建/来源回归、5安装CLI预览检查通过**；4 shell/4 Python语法及diff检查通过。安装CLI明确无系统操作；未在真实主机执行新host install.sh、validate.sh或依赖安装。
- `systemd-analyze verify`通过新service/timer；仅出现本机原有xfs_scrub CPUAccounting废弃警告，原日志保留，无新增unit语法错误。它不等于真实boot启用或安装完成。
- 最终原样生产恢复Python的**两场景隔离systemd验证通过**：新root临时unit使用实际ProtectSystem=strict/ProtectHome/PrivateTmp/NoNewPrivileges/AF_UNIX及CAP_CHOWN、CAP_DAC_OVERRIDE，四个固定路径绑定到本案root私有目录；ExecStartPre先核验目录device/inode确属影子目录。恢复模块逐字节等于冻结来源，无产品路径环境覆盖。
- 两场景都先生成**合成发布账本/惰性控制工具字节**，持锁保存真实检查点，再模拟旧安装器覆盖三文件。另一个临时服务此时运行recover，确实锁忙跳过且索引/目标/回执不变；无继承锁的root install CLI被拒绝，有检查点的rollback安装保留runner通过。
- 场景一只对本案MainPID发SIGKILL，实际Result=signal/ExecMainStatus=9，ExecStopPost独立恢复三文件及权限、failed状态；场景二不配置poststop，强杀后保留持久state，重建**仅本案影子**两个/run目录，再用新oneshot恢复。两案原合成账本及应用哨兵SHA不变、root回执准确、活动索引移除。没有重启/断电真实工作站，没有真实应用安装、恢复或数据库迁移。

这两场景验证了实际systemd的进程退出钩子、沙箱写权限与易失目录丢失后的新进程恢复；**不能替代已安装服务的真实启动、正常生产发布权威历史、真实Authentik/UI/磁盘整链，或物理冷启动/掉电验收**。当前目标禁止自动重启/断电，不为取得证据破例。

## 保留的失败、原件与范围

首轮相关测试31项中4失败/1错误，原因是临时libexec继承了组可写umask，新检查器按设计拒绝；只修正fixture的0755，未放宽生产权限门禁，后续31/46/最终47通过。记录了中间所有测试日志。静态命令预检发现本机systemctl不支持--json=short，改用固定show字段；没有运行失败的生产安装。审查补上直接installer共用锁；失败状态措辞也改为不提前宣称恢复成功。

前两批systemd探针均通过，但初版观察器只比较3个WhaleDeck应用容器，不宣称证明8个数据库服务未变。4eb4d15补齐11核心完整ID/镜像ID/StartedAt/health后，以全新独占测试资源验证通过；旧原件逐字保留，不重做生产操作或把观察缺口写成原先已通过。

root私有忽略证据：`deploy/artifacts/platform-control-candidate-1176084/`（12冻结来源、日志、SHA索引）；初版两批`platform-control-systemd-923369176c7f459789328dc3a2d96a0f/`、`platform-control-systemd-336f1e4ff12f4546a1ac3c4436b5538d/`；最终完整观察器`platform-control-systemd-efe38709bdc449dc8efa669410f6e375/`。最终来源/检查报告另见`deploy/artifacts/platform-control-candidate-4eb4d15/`。证据目录0700；导出的源码/日志/索引0600，实际测试工具0750/快照0600由上级0700隔离，无凭据入库。

## 继续执行的顺序

1. 先完成固定控制工具/独立runner/boot单元的受控安装交接与逐字节、权限、锁和维护入口验收；不能直接假定旧生产shell已会调用新记录器。当前生产新runner/账本/boot单元均未安装或创建，错误previous记录原件未修补。
2. 在六组件真实一致的旧运行基线建立可信账本，再按授权正常dev覆盖，验证current是实际新来源、previous是真正上个成功版本，以及状态/维护入口、身份权限UI、真实文件/HDD、配置与数据库保护。不得自动回滚dev，不执行数据库迁移降级。
3. 安装后的受控进程中断、重试/同版/混合来源拒绝、标准bootstrap/残留与空间预算仍须补实际验收；物理冷启动/掉电属于未验收范围，不靠合成账本或临时unit补签。
4. 完整P0-05保持部分实现，其他ACL故障、递归/历史恢复、可恢复删除与长期清理，以及UI/C#酒馆/统一供应商/AI Live2D和其余当前59项继续；17项移出不算完成，开发user NOPASSWD:ALL保留，产品Agent不扩权。主项状态、整阶段验收及25%–35%旧粗估不因本批分项上调。

文档复核：8项范围测试通过，7份变动MD的778条相对链接无缺失；目标第1节及旧可信记录QA原正文逐字保持，git diff --check通过。没有重跑前后端全量或宣称新提交整条CI通过。
