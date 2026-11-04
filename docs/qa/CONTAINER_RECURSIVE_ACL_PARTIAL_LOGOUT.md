# 递归ACL部分写入中正常退出与跨管理员处置实机验收

2026-11-04，承接[Partial取消及处置](CONTAINER_RECURSIVE_ACL_PARTIAL_REAL_UI.md)。**新资源上首项ACL已修改时，由原用户菜单正常双端退出，取消POST为0；产品停止helper并保留Partial保护，另一管理员明确处置后释放保护。主过程和独立核验通过。** 账号/组没有撤权或停用，不用删除身份或权限自然过期来触发结果。

## 启动失败及修正边界

首次工具65d4185，unit `wd-qa-recursive-acl-partial-logout.service`，Invocation `2279b6f40d7c4fb8aa807562c97d753b`，退出1。造树循环把场景kind从logout覆盖为File，浏览器严格场景检查在startup拒绝，尚未启动Chrome/真实登录/产品预检或写入。两个已创建临时身份由原上下文精确删除，自有容器已移除，未验证源先保留。

8b9691e把造树独立为函数、使用entry_kind并补回归；离线验证临时文件形状/模式及main不再用kind作循环目标。对原失败只读核对：scope原logout和baseline错误File保持、原意图数0、没有registered/ready、全部500旧文件原字节/九字段及11核心/两宿主保持。完整自有树与original.bin匹配原配方/身份后才精确清理，归档原退出/日志再停止/reset失败unit。原失败不改绿，没有重放已发生的登录或ACL动作。

## 修正后真实场景

实际工具冻结8b9691e，产品仍 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，无新发布、迁移、Agent/维护重启或回滚。新独占非root/无网络/只读根容器和四对象树，两个新管理员凭据仅stdin，正常Authentik→Vue生成计划并确认。仅停启同Worker安排切点，安装helper和源码闭包在运行前逐字节核对。

观察器核对首项child Completed/原ACL历史与真实access变更，其他三项及Root完整原样，绑定原容器root FD、操作锁FD、固定transport/cgroup与pidfd后暂停准确helper。原进行中页面从用户菜单正常logout：POST302/303、provider end-session导航、BFF401、身份端401/403、最终URL `/login`；原任务region消失、opaque引用null，原任务匿名GET401。没有发送cancel、ACK、上传或再次确认；owner浏览器result明确为null，不把父过程SQL快照伪装成退出后的私有GET响应。

开始可见退出到helper结束为4.940秒，验证器没有杀helper。前后准确身份PK/UUID/用户名、active与组列表一致，`.change()`未调用；原正权限确认时仍剩7156.210523秒，不能解释为组撤权/账号停用或过期。身份只在全部产品和页面证明完成后删除。

原任务收敛Partial/Root Partial，publish_barrier=true、completed为空、lease双空，执行Job仍Running；原意图及Job的取消时间均为空。实际五计数 **1完成 / 1修改 / 0中止 / 3未开始 / 0待证明**。与上一批取消的1/1/1/2/0不同，按本批真实结果保存，不凑相同计数。第一文件access目标/模式/ctime真实改变，内容、inode、UID/GID、nlink、size、mtime、default与普通属性保持，其他三个对象完整原样。

第二个独立管理员从真实页面进入共享异常处置，原owner接口404，五计数/原ID可读。先“暂不处置”ACK0，父过程重读完整SQL/源/Root/HDD完全不变；再一次明确确认，Acknowledged/Job Failed-phaseAcknowledged、outbox完成、保护释放。原执行ID/原Started/计划/绑定、Partial事实及实际ACL修改保留，Root与HDD原字节完全相同；唯一partial-acknowledged审计主体为第二管理员，结果Partial。没有回滚、续做、删除对象或历史。第二原上下文正常双端退出及匿名共享GET401通过。

## 独立核验、截图与运行状态

独立只读过程重读永久SQL/Job/outbox、Root/受信HDD、源计划/版本/五计数/outcome/history/manifest、准确原身份与未过期证明、两正常退出、不同主体审计、5张320×600明暗PNG及准确清理，全部通过。新验证器也只读复核上一批取消原件，未重跑原操作或覆盖其独立报告。源字节由父过程清理前完整核对，独立过程不声称清理后重新读取源。

截图边界如实保留：原owner退出截图捕捉到`/login`的SPA启动遮罩，只能配合原URL/双端状态/引用清理证据，不拿它证明登录表单已可交互。c7474a9给后续截图加“启动层结束且登录标题可见”条件；另外在全新匿名上下文做一次只读UI补核，真实登录标题/按钮可见、无横向溢出、私有递归ACL请求0、BFF401，未登录/新意图0/写入0，结果明确`originalLogoutContext=false`。新匿名图片单独保存，不替换原图，也不补签原上下文表单截图。已查看原启动层、另一管理员五计数和新匿名登录表单图片。

成功unit `wd-qa-recursive-acl-partial-logout-fixed.service`，Invocation `4d77798989ce4853a115d865b7e06a69`，15:39:29–15:40:50 CST；准确active/exited、MainPID0、Result success、ExecMainCode1/ExecMainStatus0。状态/完整Invocation日志归档后只停止该已完成QA unit，不停止产品服务。临时身份、准确源和容器已全部清理。

109项递归边界与8项范围、两Node脚本语法和diff检查通过；本批新增严格场景/无取消/无身份变更/非过期/无伪造owner结果及造树隔离证明。新UI补核仅匿名读页面，不能当作新的真实身份/磁盘整链。工具CI终态另行核对；无产品C#/Vue/协议改变，不把旧产品测试报告成本轮全量重跑。

旧500文件原字节SHA及九字段保持，普通Root145/HDD153不变；递归Root87→89、HDD107→109，恰好新增1聚合+1完成child Root及同样2份HDD。原始内容不进ACL元数据。当前19意图：8 Succeeded、5 Canceled、1无执行Expired、3预期中断Failed、2 Acknowledged；执行17/Job36、活动0。11核心healthy、两宿主Agent3795144/维护3598736及来源b880208保持，只有同Worker StartedAt改变；配置/账本/保留NOPASSWD与最小Agent授权不变。

私有证据分别在 `deploy/artifacts/container-recursive-acl-partial-fb9f29056cd24f1d8000c224838042ce/`（启动失败/精确恢复）与 `deploy/artifacts/container-recursive-acl-partial-a9883d258ced428fb021f0d5cd6ca715/`（成功整链/独立/旧取消只读兼容/匿名UI补核）。`anonymous-ui-check/`明确属于新匿名上下文，两批证据不混写，不进入Git。

源/HDD同device，不签物理异盘、冷启动或瞬时维护连续性。其余Partial撤权/重启、Unknown、跨写入保护与路径/磁盘/依赖故障、递归ACL历史恢复、可恢复删除/长期清理及其余59项继续；本场景分项通过不提升整项/阶段完整签收计数。
