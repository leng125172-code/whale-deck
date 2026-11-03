# ACL历史恢复写后同CID实例变化：候选与实机验收

## 候选记录（2026-11-04，尚未实机运行）

新增恢复profile8/kind post-acl-instance-change、private proofVersion6，独立于目标移除profile7/proofVersion5。validate_container_acl_restore_unknown.py接受显式post-instance；无参仍严格保持目标移除，其它/混合参数在任何故障动作前拒绝。普通Unknown proof2/3/4、旧恢复profile7及terminal fault验证边界保持，实例模式不能由普通或目标移除证据冒签。

计划先用全新真实Authentik/Vue来源捕获与历史选源恢复，在目标ACL已写、Root仍Applying的精确切点，检查其它Job/终端/锁/屏障/九类文件意图0及仅本轮helper活跃，再对本轮独占容器执行一次固定docker restart --timeout 1。要求CID、镜像、普通UID/无网络/只读根与全部挂载安全配置保持，PID/StartedAt确实改变；重启前后host bind及新实例/proc命名空间完整清单一致，原计划/来源/执行身份不能改写为新实例。

复用不同管理员真实异常面板、暂不处置零确认、原版本一次明确ack、跨容器目录4次自然延期及处置后才写入。Root Unknown/aclConfirmed null/无发布版本、来源/原HDD/迟到apply保持；业务Acknowledged与执行Failed不代表恢复成功。owner/recovery UI、摘要、baseline和来源provenance的实例标记互相核对，缺少实例转换三份回执、旧PID/启动时间、改镜像/挂载或非零其它工作均拒绝。管理员面板来源专属展示仍待，不能用脚本变量补签。

121项相关检查通过（9恢复Unknown、22普通Unknown、28恢复来源、44Linux-root范围、10恢复浏览器证明、8范围），Python/JS语法及git diff检查通过；包含profile7/8、proof5/6、CLI模式混配拒绝和Unknown审计/ack事实保持，原真实实例转换的负例继续执行。它们是隔离/合成输入，不是本场实机通过；本节为冻结前记录，实际结果后续接续。

产品仍b69302b/1.52，Agent1772448/维护1688513，不为验收工具重发生产、不重启宿主或宿主服务。仅本轮明确标记的容器可重启，不操作其它容器；旧失败源树及Root/HDD保持。user NOPASSWD保留、Agent最小授权不扩大，不自动回滚dev。
