# 目录移动：目标已发布、部分原件留存中断与真实Unknown处置

2026-10-30，实际产品仍为 `a59ec973478471745a1b7a5aa41da16dae20d426` / Agent 1.46；验证器冻结 `af9b5c1`。本次没有产品安装或容器重建发布；仅在独占测试容器执行真实故障与页面验收。单工作站59项继续，17项移出不计完成。

## 实际场景与切点

独占无网络、只读根、非root 1000:1000测试容器，唯一可写bind内创建16份各2,097,159字节文件（总33,554,544字节）与空目录，混合UID/GID、0600/0640、固定纳秒mtime。正常Authentik临时管理员登录、BFF登记预检、明确确认、永久SQL双任务、Worker、签名UDS及安装态Root helper完整执行。

Root观察器必须匹配原SQL执行ID/owner、完整目录内层计划、准确容器实例、固定安装apply argv，并通过pidfd绑定同时持有准确容器root FD和原操作锁FD的进程。仅在真实SourceRetaining阶段停止，核对目标完整发布/目标inode、私有留存根inode、原件在公开源与私有留存两处恰好互斥分布。实际切点为1份原文件已转存、15份仍在原源目录，16份目标文件全部存在。

Python父进程在进程停止后独立读取全部目标与两处原件的所有字节并核对SHA256，以及原文件inode和普通8项发布身份、目标UID/GID/模式/纳秒mtime。随后只通过这个确切pidfd强杀本次helper，不修改Root记录、SQL、租约或时钟，不安装产品暂停钩子。

## Unknown与跨资源保护

原任务经产品自行观察进入Unknown、保留发布屏障及privateMayExist；SQL和永久Root三态事实为 `[true, null, null]`：目标发布有完整证明，源移除/完整原件留存未核实，不用false掩盖部分状态。原公开源实际仍存在，私有原件区也确有部分文件；本轮未把部分留存表述成完整留存。

原管理员正常退出，临时身份context按准确用户ID删除。另一实际容器提交新建目录任务，至少两个Claim/Defer循环保持Pending、RootStarted为空、没有目标；原移动Unknown屏障持续存在。

## 当前管理员真实手机页面处置

另一管理员通过实际Authentik用户名/密码登录320px暗色页面，无响应替换或登录态注入。容器移动异常面板准确显示原任务和三项事实：目标发布“已确认”，源路径移除及原件留存“尚未核实”。先选处置再“暂不处置”未发确认；重新检查后明确确认，仅一次POST。

处置结果Acknowledged，原执行Job以Failed结束、publishedVersion仍null；不是移动成功。原请求/计划/执行绑定未变，原Root Unknown字节不变，已知true和两个null未改写，原审计唯一且指向新管理员。只有屏障解除后，另一容器的目录任务才真正开始并成功。

手机确认/完成截图均人工复核：没有横向溢出，长CID/任务ID换行，事实说明与底部确认按钮完整可见。正常双端退出及私有控件清空通过。仅签收此处置页面，不据此声称全站所有页面/可访问性已验收。

## 晚到原Apply栅栏与准确清理

保持原容器和文件现场，向安装态固定Root helper再次发送原operationId/owner/plan：仍返回Unknown及原三事实，不继续转存或删除源，不重做移动。原Root永久字节未改变，目标与原源/私有文件的全部字节和逐对象元数据与切点观察完全相同。此项是固定helper晚到请求验证，不冒称新增签名RPC重放验收。

之后仅对已完整证明的两棵合成源/目标与私有原件按准确名字/inode/内容逐项清理；两个准确测试CID与源目录不存在。永久Root/SQL双任务/审计、清单及截图保留。第三个只读进程重新核对原记录、内层摘要/manifest域、16份可重复合成SHA和原inode、页面回执、新管理员审计、等待任务启动时间不早于原处置完成、原helper晚到结果、确切清理及身份。

最后全局移动10条：4成功、3取消、2中断失败、1Acknowledged；活动/屏障/租约0，临时身份0，活动transient unit0，11核心healthy。清理后的Root三态与privateMayExist是不可变历史观察，不宣称测试文件仍物理存在；后置只读检查也不冒称读取了已删除的物理字节。

## 证据与边界测试

- 实际首轮通过：`deploy/artifacts/container-move-unknown-0436d8f86401493bada98623030a3d9a/`，包含baseline、bound-cut、physical-at-cut/final、unknown-root、blocked、browser-result、acknowledged、late-root、verified、independent-final和两张手机截图。
- 实际日志 `/tmp/wd-container-move-unknown.log`；只读独立复核 `/tmp/wd-container-move-unknown-independent.log`、补充时间解析/活动unit检查后的 `/tmp/wd-container-move-unknown-independent-second.log`。
- 新留存切点9项与原部分文件切点14项实际Linux-root边界测试全部通过；原有容器移动47项root回归通过。新观察器已接CI，未用这些测试替代实机。

## 上一提交CI范围检查失败

保留原GitHub运行 `40848098427`：frontend/backend/native-postgres/native-valkey成功，installer在8项范围检查的元数据固定措辞断言失败。原 `.project-atlas.json` 的nextStep写为“17项移出不计完成”，检查要求“17项移出不算完成”；两者语义一致但字面不符。已恢复既有标准措辞，范围仍为59项保留/17项移出，没有删除或放宽检查，也没有修改运行服务。

实际下载原job日志后，在工作站逐命令重现相同失败，原日志 `/tmp/wd-ci-installer-original.txt` 和本机失败 `/tmp/wd-installer-ci-local.log` 保留。修正后的8项范围检查通过；完整当前installer命令集重新执行、退出0，41组共1057项（明确跳过0项，实际执行通过1057项），shell/Python/Node语法检查也通过。命令集逐字核对当前workflow一致，日志 `/tmp/wd-installer-ci-local-fixed.log`。这是本机重现与修复验证，不把前四作业成功或本机验证冒充原整条GitHub CI成功；新提交CI另观察。

## 仍需继续

本轮是同HDD上的目标发布后部分原件留存故障，不替代发布交换前后全部切点、源目录最后移除阶段、撤权/自然权限到期组合、跨盘BFF和全部浏览器矩阵。继续容器删除/权限等剩余写操作、完整资源隔离与长期清理，并推进其余59项，不把这个子门禁签成完整P1-11/P2-03。
