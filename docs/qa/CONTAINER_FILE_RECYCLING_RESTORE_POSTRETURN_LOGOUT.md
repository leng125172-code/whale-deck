# 独立恢复：原件归还后的正常双端退出

2026-11-06，冻结工具 `01f05323ca12dbedde38686792df8f7635919284` 在保持3d80d17部署上，以全新自有来源和临时真实Authentik管理员，完成写后正常退出。只在本Root Applying、原来源Restored、原inode已回目标且最终journal未落盘的准确切点触发退出；不是重做终态后退出，也不以取消/撤权代替。

unit `whaledeck-qa-restore-postreturn-logout-first`，Invocation `7af5e0e6195d42018e0ba695698338be`，exited/0；清理前后独立进程均passed。4项新专项结合回归共132项Linux root通过、零跳过；8范围和Python/Node语法通过。只签这个写后退出组合，其余矩阵与59项交付未完成。

## 准确触发与原进程退出

Vue先回收全新1 MiB+7字节文件、独立核对SQL/原Root/原件/HDD，再以新ID预检和确认恢复。固定helper由MainPID/Invocation/cgroup/确切argv、容器root/恢复锁/原来源锁三个FD绑定，暂停后重验来源claim、目标原inode、trash缺席、当前来源Restored和本Root Applying，父进程重开实际HDD元数据/数据后才放行页面动作。

原helper PID2788295、Invocation `e1462467e43a4502a12205ec305c1548`。浏览器关闭原对话框/抽屉，经原用户菜单正常退出，仅在真实BFF导航POST发出后写观察标记；原pidfd在该标记被观察时仍存活，约5.847948秒后观察退出。计时在放行页面前开始，含HDD核对和关闭界面/菜单交互，不是只测退出HTTP响应，也不以晚到PID查询补签。

两次正常退出均实际观察BFF POST302、OIDC end-session导航、BFF当前会话401、Authentik当前身份403及最终登录页。第一次发生在写后执行中；同Subject可见重登后，原恢复意图、原键、原回收意图三个Owned引用均404，当前管理员RecoveryGet200读取原历史结果，再正常双端退出一次。

## 成功事实与独立清理

同一意图/执行最终Succeeded、`CONTAINER_RECYCLE_RESTORE_COMPLETED`、真实回执Completed，bindingCurrent/historyVerified/historyAvailable=true；sourceState=Restored、sourceRetained=false/sourceRestored=true/restoredByThisOperation=true。RootStarted/计划/执行/完整来源绑定保持，成功来源claim保留且匹配本恢复；Outbox0、租约清空、保护释放。取消POST/显式取消审计0、取消时间为空。

正常BFF退出撤销旧登录绑定，但原件已归还，不抹掉已发生的写入。这里Native ContainerRoot文件系统session不是浏览器BFF登录会话；最终成功仍需真实原容器/实例/目标inode及HDD可核验。本批未通过容器消失或不可证明当前绑定的Superseded替代Succeeded。

新登录的实际Jobs页取准确take=100列表原执行，17字段成功/Completed摘要、正确原来源和本操作已恢复事实吻合，两张真实PNG保存暂停界面和原Job。普通用户5 GET/4 POST均403，匿名5 GET均401；临时身份精确移除，其他真实用户未变更。

独立进程重读SQL/回执/Root、成功claim、来源/HDD实际数据与当前归还原inode，关联暂停时Applying原件后，才删除本轮已恢复的自有叶文件/空私有目录/容器；清理后再核永久历史/HDD及资源缺席。没有再次归还或删除永久历史。原回收业务仍保留历史Succeeded/Trashed，不把当前Restored物理状态改写进旧Job。

## Job页尾提示修复候选（未部署）

检查本轮真实Job截图发现：回收/独立恢复任务也显示递归ACL专属提示。`f51f4735d5be82b2bc11c9c56fa2bcb85106a662` 单独区分页尾提示，回收/恢复使用其对应来源/预检与历史核对说明；原广义文件任务的取消/通用重试保护条件保持不变，未因文案分类开放通用重试。

定向JobsView单元25项、vue-tsc、oxlint/eslint、Prettier和本地Vite生产构建通过；增加独立恢复两类型到“不显示通用重试/不请求retry”的测试，并验证两类提示不会互相串用。本机Corepack缺固定pnpm入口，已使用仓库现有依赖CLI；Node22.22.1满足package声明范围，未下载/升级依赖或声称包管理器已验证。首轮测试条件expect被lint拒绝，改成必定执行的断言后通过，初稿及工具错误记录保留。

该文案修复尚未取得准确CI终态、标准部署或真实新版本页尾复验；本轮所有实机退出结论对应旧部署3d80d17。不能用本地单元/构建把截图里的旧提示补签为已修复生产页面。

## 保持与剩余

2346保护、11核心、应用及两宿主3d80d17保持，无部署/服务重启；旧28来源/14恢复完整行SHA保持，新增1来源/1Succeeded恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root56→58/HDD59→61、恢复Root35→38，仅本执行json/lock/成功来源claim新增。

源/HDD同device2049，不算异盘灾难恢复；本轮未采样维护连续性。继续页尾候选准确CI/标准发布/复验、写后实时撤权、Unknown/来源丢失/实例变化/跨写入及长期清理和59项。17项移出不算完成，25%–35%仍为粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。

私有封存位于deploy/artifacts：真实退出 `container-recycle-restore-postreturn-logout-4e0bd5fd8c72400b9bef086627e72ffe`，161文件/115源码，`POSTRETURN_LOGOUT_SHA256.json` 摘要 `af2ae0508014f647d5d8a328c161474b478e40dc015c26cf7f42635a887b3d93`；未部署页尾候选 `container-recycle-restore-job-footer-e0bc18a7defd4889aba0d4e378df0fd4`，5文件，`FOOTER_CANDIDATE_SHA256.json` 摘要 `f4a0c68057791e7d47a817f9407022b627edbe5959a6ef5c9b2a098cf54d1701`，含lint初稿/工具错误及最终源码和检查边界。私有配置、凭据与身份原件不入仓库或图谱。
