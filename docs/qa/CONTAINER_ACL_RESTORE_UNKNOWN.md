# ACL历史恢复Unknown：候选与实机验收

## 候选记录（2026-11-04，尚未执行实机）

新增独立validate/verify_container_acl_restore_unknown.py，复用已验普通ACL Unknown的准确切点、原上下文正常退出、不同管理员明确处置和跨容器目录保护；产品仍b69302b/1.52、Agent1772448/维护1688513，本批无发布、迁移、权限或协议变更。

本次范围严格限定：全新普通UID/无网络容器和HDD源，真实Vue先生成本轮捕获，再经公共历史页面选源、登记新恢复并明确确认。在恢复目标ACL实际写后且Root仍Applying时只移除本轮准确容器，源文件保留；产品结束原helper并留Unknown，不能据已观察写入把Root回执篡改为成功。原登录正常双端退出后删除本轮身份，不复用旧操作。

恢复profile7/private proofVersion5与普通proofVersion2/3/4及其它恢复fault schema7–12分开，严格冻结全部依赖和安装helper；不能把写前、实例变化或普通ACL捕获冒签为该模式。来源捕获、selected captureId/historyDigest、canonical restore_source_json、目标条目和原登录/请求/计划/执行hash在Unknown与Acknowledged前后核对。恢复审计只接受containers.files.acl.restore.*，result及unknown-acknowledged均为Unknown；ack演员必须是当前不同管理员，historyRestore/restoreSourceHash必须匹配。

不同管理员使用现有共享Recovery API和真实异常面板，先暂不处置保持Unknown/保护，再以原version明确ack一次。另一容器目录应在4个不同自然重试版本中均无Root/目标，只有ack释放后原ID才写入并在Vue可见。Root Unknown/ACL确认null/HDD和迟到原Apply保持，业务Acknowledged/执行Job Failed不算恢复成功。正常任务摘要/Unknown与ack两阶段任务抽屉验证真实API/SQL安全结果，禁止通用写按钮和额外写入。

管理员通用异常面板当前不展示captureId或恢复来源专属字段；候选仅证明原Owner真实选源/恢复wrapper及服务端来源绑定，不把脚本变量当作管理员来源UI。来源专属展示、写前/实例或来源变化、磁盘故障、其它共享写入、递归ACL/恢复和删除仍待。

118项检查通过（6恢复Unknown、22普通Unknown、28恢复来源、44Linux-root切点/清理范围、10恢复浏览器证明、8范围），Python/Node语法与git diff检查通过；新测试进入CI安装检查。它们是隔离文件/合成证明输入，不是实际身份/Worker/文件/HDD/页面通过。本节为冻结前记录，实机结果在后续章节接续。失败保留未证明源和Root/HDD；fallback只允许本轮原ID走普通当前管理员BFF ack，不能使失败门禁变通过。user NOPASSWD保留，Agent最小授权不扩大，不自动回滚dev或重启主机。
