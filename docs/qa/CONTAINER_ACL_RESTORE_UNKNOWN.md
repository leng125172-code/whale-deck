# ACL历史恢复Unknown：候选与实机验收

## 候选记录（2026-11-04，尚未执行实机）

新增独立validate/verify_container_acl_restore_unknown.py，复用已验普通ACL Unknown的准确切点、原上下文正常退出、不同管理员明确处置和跨容器目录保护；产品仍b69302b/1.52、Agent1772448/维护1688513，本批无发布、迁移、权限或协议变更。

本次范围严格限定：全新普通UID/无网络容器和HDD源，真实Vue先生成本轮捕获，再经公共历史页面选源、登记新恢复并明确确认。在恢复目标ACL实际写后且Root仍Applying时只移除本轮准确容器，源文件保留；产品结束原helper并留Unknown，不能据已观察写入把Root回执篡改为成功。原登录正常双端退出后删除本轮身份，不复用旧操作。

恢复profile7/private proofVersion5与普通proofVersion2/3/4及其它恢复fault schema7–12分开，严格冻结全部依赖和安装helper；不能把写前、实例变化或普通ACL捕获冒签为该模式。来源捕获、selected captureId/historyDigest、canonical restore_source_json、目标条目和原登录/请求/计划/执行hash在Unknown与Acknowledged前后核对。恢复审计只接受containers.files.acl.restore.*，result及unknown-acknowledged均为Unknown；ack演员必须是当前不同管理员，historyRestore/restoreSourceHash必须匹配。

不同管理员使用现有共享Recovery API和真实异常面板，先暂不处置保持Unknown/保护，再以原version明确ack一次。另一容器目录应在4个不同自然重试版本中均无Root/目标，只有ack释放后原ID才写入并在Vue可见。Root Unknown/ACL确认null/HDD和迟到原Apply保持，业务Acknowledged/执行Job Failed不算恢复成功。正常任务摘要/Unknown与ack两阶段任务抽屉验证真实API/SQL安全结果，禁止通用写按钮和额外写入。

管理员通用异常面板当前不展示captureId或恢复来源专属字段；候选仅证明原Owner真实选源/恢复wrapper及服务端来源绑定，不把脚本变量当作管理员来源UI。来源专属展示、写前/实例或来源变化、磁盘故障、其它共享写入、递归ACL/恢复和删除仍待。

118项检查通过（6恢复Unknown、22普通Unknown、28恢复来源、44Linux-root切点/清理范围、10恢复浏览器证明、8范围），Python/Node语法与git diff检查通过；新测试进入CI安装检查。它们是隔离文件/合成证明输入，不是实际身份/Worker/文件/HDD/页面通过。本节为冻结前记录，实机结果在后续章节接续。失败保留未证明源和Root/HDD；fallback只允许本轮原ID走普通当前管理员BFF ack，不能使失败门禁变通过。user NOPASSWD保留，Agent最小授权不扩大，不自动回滚dev或重启主机。

## 全新资源真实门禁及独立复核通过（2026-11-04）

冻结工具`f0226dd4b50f41465c6fcc01f03067354689618b`，51份依赖逐字/SHA及实际安装helper核验。主门禁validate_container_acl_restore_unknown.py b69302b…退出0，首次完整实机通过；原件`deploy/artifacts/container-acl-unknown-73afc597c1e34895a6607f00e5430d1f/`。产品保持b69302b/1.52，无发布、迁移、宿主重启或自动回滚。

两套全新独占容器/HDD源，原用户正常可见Authentik→Vue完成本轮普通来源捕获→公共历史List/Read选中真实旧基本ACL→新恢复预检/明确确认→永久PG/Worker/签名Agent。目标ACL已写、Root仍Applying且当前前像HDD已验证时，只移除准确本轮容器，完整源保留；原helper确认仍活后正常用户菜单双端退出，产品4.811秒结束helper，未由验收器杀helper。所选旧来源、canonical restore_source_json和所有原请求/计划/执行/登录绑定保持。

原身份随后删除。业务Unknown、执行Job Running/保护保留，Root Unknown、aclConfirmed=null、publishedVersion=null；不能把现场已观察到的基本ACL写入当成已证明恢复成功。不同新管理员真实320×600暗色面板先暂不处置、确认POST0，随后只发一次原version明确ack。恢复专属4个原动作+1个unknown-acknowledged审计、不同ack演员及historyRestore/restoreSourceHash逐字段核对，普通ACL前缀或Succeeded结果不能代替。

另一容器目录按原ID自然延期4次，版本2/4/6/8，各轮Root和目标均不存在，Unknown保护与源保持。00:09:56.537648+08:00明确处置后，原等待任务于00:09:59.930647+08:00才实际Root写入，最终Succeeded并在Vue文件列表可见。原恢复业务Acknowledged/原执行Failed，Root/HDD仍Unknown，null确认及无发布版本保持；迟到原Apply返回Unknown/replayed，未重写目标、原Root或HDD。

1份本轮来源+1份当前ACL原件分别验证；两文件各2097159字节、原inode/属主/mtime/普通属性和非目标ACL按每轮完整核对，目标ACL/ctime按已写切点单列。9张本批真实PNG：原进行中、Unknown目标/确认/处置、Unknown与ack两阶段任务标题/事实、等待目录可见；任务摘要与实际API/SQL逐字段相符，ACL确认“未证明”、回执Unknown、绑定无效、无发布版本明确显示，无通用取消/重试写按钮，任务页面写请求0。手动复看移动端确认动作区及ack事实，纵向滚动保留、按钮完整入视口。原Owner真实选源/注册wrapper和不同管理员Owner接口404已验证；管理员面板仍是通用ACL异常展示，不冒称captureId或来源专属UI已经提供。

原用户与恢复管理员均在各自上下文正常双端退出；恢复管理员另一个用于登记目录的HTTP会话也正常退出。独立verify_container_acl_restore_unknown.py退出0、independentAclRestorationUnknown=true，重读永久SQL/Root/attested HDD/来源/审计/Job、4自然延期与目录真实发布时间，确认准确源目录、容器及临时身份不存在。它不在清理后重读已删除源字节，完整物理证明来自主验逐轮读取；未为独立通过重做写入或ack。

最终只读六组件probe仍b69302b；Agent1772448/维护1688513保持，11核心healthy，8数据库平台及API/Gateway的ID/镜像/StartedAt保持，Worker仅为精确切点正常停启。7份配置/能力密钥/sudo文件SHA/uid/gid/mode和可信账本保持，活动Job/终端/锁/屏障/意图0。前批131份HDD元数据字节及九字段逐份保持，库存133只新增本次来源/当前前像2份。三批旧失败源树保留，不算长期清理完成。预检/最终边界在`deploy/artifacts/acl-restore-unknown-preflight-28a0fb7eb63a4dbdbf5613f5f9b54033/`，原件root私有并保留SHA清单，凭据不入仓库或日志。

本批仅签**写后移除目标容器**、不同管理员处置及跨容器目录延期/释放。写前、同CID实例变化、来源丢失/替换、磁盘/其它共享写入、恢复来源专属管理员UI、递归ACL/恢复与删除/长期清理仍待。源和HDD同device，不算异盘灾备；59主项及六阶段完整签收数不提升，17项移出不算完成，master未交付。

收尾8项范围测试、878条变动MD相对链接通过，目标第1节逐字保持，2份QA原文仅追加。准确工具[CI run41670846073](https://github.com/leng125172-code/whale-deck/actions/runs/41670846073)查询首次EOF、重试确认in_progress，未签成功终态；本机118检查和本批实机通过分别记录。项目图谱接口两次传输失败，稳定事件`whale-deck.container-acl.f0226dd.restore-unknown-shared-barrier`保存在本地pending待同步，未声称网站更新成功。

## 后续接续：同CID实例变化

以上移除目标组合与当时图谱连接失败记录保留。图谱原事件随后确认成功、按同一eventId移入synced。b33ad69再以全新资源完成写后同CID实例变化加正常退出、不同管理员处置/目录自然延期释放、真实页面及独立复核，见[本轮实例变化](CONTAINER_ACL_RESTORE_INSTANCE_CHANGE.md)；来源异常、来源专属UI及其它目标仍待。
