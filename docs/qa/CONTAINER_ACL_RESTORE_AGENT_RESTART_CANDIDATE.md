# ACL历史恢复写后Agent重启：门禁候选

2026-11-03。产品仍`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/1.52；历史超时修复b69302b四镜像已预构建，准确CI前端最后页面组仍在运行。**本批仅扩展验收工具，尚未部署b69302b，未运行新的恢复意图或实际Agent重启，不计真实整链通过。**

## 工具覆盖和安全边界

恢复门禁新增独立profile6，对应验收来源schema12，仅接受`post-acl-agent-restart`单场；不能混用写入前Agent profile3、写后Worker profile4或写后撤权profile5。schema12是私有验收格式，不是生产数据库schema、迁移或Agent RPC minor变更。旧profile1–5/schema7–11和普通ACL schema6仍按原条件核验。

计划执行链仍为全新独占容器/完整文件与普通属性、正常Authentik/Vue产生本轮ACL捕获，再从公共8080历史列表和快照选择旧ACL、登记新的恢复预检并明确确认。原Owner恢复读取必须返回真正`{intent,source}`，不能拿普通ACL读取或其它来源替代。两份来源和当前前像分别绑定原SQL/Root/attested HDD，原捕获不能复作恢复意图或执行。

复用既有准确切点：只在本轮已写目标ACL、Root仍Applying且当前前像HDD已验证时观察原helper；重启前需全部其它活动任务/终端/锁/屏障/文件意图为0，并核对精确PID、cgroup和两份内核FD。只允许固定Agent服务重启，产品自行结束helper，验收器不杀helper；不重启维护服务或主机，不编辑SQL、时钟或凭据。

最终事实预期是原ID的Succeeded/Completed和新ACL/版本保持，不把进程中断伪装为Canceled或Failed。核对完整源、九字段、两类ACL、普通属性、所选旧历史与本次当前前像、来源/授权hash保持，迟到原Apply只读回原完成回执、不再写目标。独立核验保持相同恢复审计前缀与Succeeded结果，拒绝额外cancel事件。

增加恢复重启的明确wrapper/source检查，并沿用普通写后Agent已具备的任务中心双原Job摘要、只读详情、敏感字段不显示和禁用通用写按钮证据。要求正常原上下文双端退出，320×600暗色状态/任务页可见；固定维护PID/source保持，重启前/中/后入口200、healthy/Ready、no-store且采样连续。Agent重启不冒充Worker强杀后的自然租约等待，仍核对原执行和授权/来源hash。

失败仍保留未证明源树、原失败截图和SQL/Root/HDD；注册状态不明、活动租约/屏障、Unknown或元数据不符均不能清理。不得为通过而重放旧批次、改Root回执或跨资源操作。user NOPASSWD保留，产品Agent授权不扩大。

## 本批实际验证

25项恢复来源/格式/审计/恢复wrapper与维护连续性证明测试、44项Linux-root切点/故障/精确重启范围检查、10项恢复浏览器证明及8项59项范围检查，共87项通过，无跳过。新增反例拒绝schema11/普通6冒签、版本布尔/错profile/错来源/普通读取路径、错误终态、额外取消、来源与授权hash变化、维护503。只读复核确认恢复与普通ACL使用同一安全Job摘要，新增显式恢复profile6摘要兼容及错误JobType拒绝用例。Python警告按错语法检查、两份JS语法与git diff检查通过。

这些测试用隔离临时文件或合成证明输入，不是实际Authentik、进程重启、恢复页面、SQL/Root/HDD或公共8080修复验收。前批4363 Unit/488 Integration等是b69302b产品候选证据，本批没有重复执行并冒充新工具的全量后端结果。

## 下一步与依赖

先取得b69302b准确CI五路成功终态、标准覆盖及六组件/安装helper/健康/账本证明，再用新资源完整复验写后移组和账号停用；随后冻结本批工具运行单独的恢复写后Agent重启门禁及独立核验。保留[超时失败记录](CONTAINER_ACL_HISTORY_TIMEOUT.md)和[四镜像发布前基线](CONTAINER_ACL_HISTORY_RELEASE_PREPARATION.md)，前批单个移组分项不等于整批签收。

远端76cb825推送及准确dev SHA已确认；前批预构建图谱事件以原eventId重试成功并从pending移入synced。历史准备文档当时的连接失败边界保留，不改写原回执。59主项整项完成率不变，17项移出不算完成；Unknown/实例或来源变化、磁盘/共享写入、递归ACL/恢复、删除/长期清理及其余目标仍待，不自动回滚dev。
