# ACL历史恢复写后Agent重启：来源摘要修正与实机验收

## 首轮失败与工具修正（复验前记录）

2026-11-03。产品保持b69302b/1.52，原Agent1688516、维护1688513。工具44a60de的profile6/schema12首次门禁在来源捕获校验阶段退出1、FileNotFoundError：该场景要求jobResultsVersion=1，普通来源捕获的verify_case因而要求`capture/access-jobs.json`，但prepareRestoreFault此前没有读取并保存这两个来源Job回执。浏览器等待父进程放行后超时退出；不是产品恢复写入或Agent重启失败。

原件`deploy/artifacts/container-acl-faults-76e36b9939ac46ac83ac4924383e7b12/`完整保留。只读SQL按准确CID查到仅1个Succeeded普通ACL来源意图、恢复意图0/活动0；两宿主PID/source保持，Agent重启未尝试。容器准确移除，未证明源树按失败策略保留，未生成整批通过回执，不重放已完成来源捕获。

修复保留全部摘要核验条件：仅profile6的来源捕获由真实原会话GET两个Job，先保存source-capture-jobs.json，再发source-captured信号。父进程核对受保护文件/大小/格式/来源ID后保存capture/access-jobs.json，随后共享verify_job_summaries逐字段比较实际SQL与API；缺失、错来源/错Job或错误摘要仍失败。独立verify_restore_binding显式从原capture目录读取摘要，observed目录只承载新观察结果，不能改写原件。旧profile1–5未设置该标记时不要求新增文件；没有删除标记、跳过SQL校验或扩大产品权限。

28项恢复证明、44项Linux-root切点/失败清理范围、10项恢复浏览器证明、8项59项范围，共90通过，Python/JS语法及git diff检查通过。新增回归覆盖缺回执/错来源或执行Job/错误成功事实、旧profile5不读取新文件、独立重验准确原目录。测试首轮新增临时文件清理重复unlink导致1错，已修正；保留失败事实，不称首轮全绿。隔离证明测试使用合成API/SQL输入，不能算实机Agent重启或页面通过。

本节记录修复提交时点，尚未登记新的恢复意图或重启Agent。随后必须冻结修复工具，以全新资源正常Authentik/Vue捕获与恢复、准确已写切点/固定Agent重启/维护入口和双原Job页面、HDD/正常原上下文双端退出及独立只读核验；实机结果在后续章节接续。产品无需为工具补证重发，user NOPASSWD保留，Agent仍最小授权，不自动回滚dev或重启主机。
