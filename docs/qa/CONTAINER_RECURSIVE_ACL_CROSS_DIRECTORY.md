# 递归ACL Unknown 跨容器目录延期与释放

2026-11-04，承接[同CID实例变化](CONTAINER_RECURSIVE_ACL_INSTANCE_CHANGE.md)。本批新增候选，尚未执行本批实机，不计完成。

新的原容器真实首项ACL修改后目标消失/正常退出，保留Unknown与原Root/HDD；另一位当前管理员通过真实Vue为另一新隔离容器规划和登记0750目录。要求四个不同版本、不同期限的自然延期：原Unknown/SQL/Root与完整物理保持，等待目录无RootStarted/租约/发布屏障或完成，目标与目录Root原件不存在，原文件保持；不改SQL/时钟或强制重试。

当前管理员先暂不处置原Unknown、再一次明确ACK；只在处置后原目录ID实际开始并Succeeded，核对原主体/登录绑定/计划/请求摘要/原ID、Root指纹/版本与真实inode/owner/mode。返回同容器页面必须显示原任务和实际目录，不能用API登记冒充真实页面登记。准确证明后才清理两套自有资源，原ACL历史与新目录Root均留存。等待计划令牌只保存SHA-256，不保存原文。

110相关工具root测试零跳过、8范围、两Node语法及diff检查通过；仍需干净冻结后的真实身份/页面/Worker/Agent/磁盘与独立核验。产品仍b880208/1.53，没有产品发布。其他跨写入/路径磁盘依赖、递归恢复、删除及其余59项继续。

## 两次原总门禁失败（保留）

51fe911首次Invocationa661784171c44d5a892271ee5aa16c8c，17:50:57–17:52:09 CST退出1，未捕获部分写入cut，原操作真实Succeeded/Completed及4/4/0/0/0。原件 `container-recursive-acl-partial-a13442c065384cee8dfc092e66c742b7`；等待容器登记0，未发生Unknown/ACK。仅只读补核SQL、5Root/5HDD、完整物理后精确清理；SQL投影明确非原浏览器完成回执，不补签正常退出。补核首次统计误用不存在resource_id，只读查询失败另存，更正后通过；539旧保护文件及45旧目录Root保持。

同源码新资源Invocationbdda06ea0eac4b5ca7fd6ec31e2afb76，17:56:19–17:58:06 CST退出1；原件 `container-recursive-acl-partial-d6a8627144bd402ea4ccb7443dc6ee8e`。实际Unknown及另一容器真实Vue计划/登记已发生，但采样时正常短暂claim租约被工具误判。修正只将双空租约的真实延期计入样本，单次领取不算延期；RootStarted/目标/Root存在仍拒绝。

失败恢复仅接受这个准确目录和Invocation：由新当前管理员真实页面明确处置原Unknown，并通过正常管理员HTTP请求取消已失去原临时身份/测试容器的旧等待任务，保持其原主体、登录绑定、计划和请求摘要；不替换主体或重登记，不计作跨容器目录发布验收。原两总失败永久保留，待新资源完整复验。
