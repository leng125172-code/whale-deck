# 容器 ACL：633a2ca 正常覆盖与真实页面/SQL/Root/HDD常规验收

后续记录：已新增[进行中取消与正常双端退出实机证据](CONTAINER_ACL_FAULTS.md)，仅签收尚未写ACL的HistoryVerified切点。本页原九场景、当时待办与来源事实保留，不重复操作。

2026-11-03。实际运行六组件 `633a2ca42012bbd6f388a67bd128fe3125bfab76`，Agent协议1.51；验收工具冻结 `0668215bc0f48e8d86d2567d9431fc5888f602ed`，21份验收依赖及11个安装模块逐字节核对。**九个常规真实页面场景首轮与独立只读核验均通过，同一原上下文正常BFF/Authentik双端退出也通过。** 不把前批[生产预览](CONTAINER_ACL_WEB_UI.md)、[隔离PG](CONTAINER_ACL_PERSISTENCE.md)或[机器RPC](CONTAINER_ACL_AGENT_RPC.md)拼接为本批证据。

这里只签收单对象access/default常规链与预检取消、权限/CSRF/Origin/幂等和实盘历史保留。**进行中取消/退出/撤权/重启/丢回执/Unknown及跨写入故障矩阵、ACL递归与历史恢复、删除/长期清理仍待**，59主项继续部分实现，17项移出不算完成，master未交付。

## 一次正常覆盖与运行来源

部署前全局Job/终端/主机锁、upload/directory屏障及copy/move/permission/ownership/两类递归活动均0，生产ACL表尚不存在，11核心healthy。运行checkout `/data/GitRepos/whale-deck` 由其所有者从干净dev/b1fafed快进到固定633a2ca，未由root写开发者Git索引、未更改私密配置。核对固定维护helper字节后只启动一次 `whaledeck-dev-publish-633a2ca.service` 的标准update，无自动回滚。

Invocation `5761b5620b27452d925f196bb134537e`，03:07:09–03:20:00 CST（约771秒）。标准宿主构建/安装、镜像构建、前向迁移与强制替换应用容器完成；RemainAfterExit终态active/exited、MainPID0、Resultsuccess、ExecMainStatus0，回执保存后仅停止该已结束单元，随后inactive/dead、MainPID0、Resultsuccess。没有因依赖恢复耗时重复启动发布。NuGet及系统包标准下载出现重试并正常结束，未注入私有缓存、凭据或关闭审计。

六组件CLI/镜像revision与实际HTTP均为633a2ca；Agent PID3122630、MaintenanceHost PID3122598的实际exe及映射DLL device/inode与新安装文件一致，非仅磁盘文件更新。生产 `20261102180208_ContainerAclIntents` 已应用、17个CHECK存在，验收前表0行。API/Worker/Gateway正常重建，8数据库平台容器ID/镜像/状态/StartedAt完全不变；最终11核心healthy，两宿主active。

标准安装validate覆盖运行库/宿主依赖、UDS/helper/HDD和维护入口配置；user NOPASSWD文件root:root/0440及visudo校验通过，前后SHA一致，密码未改。产品Agent权限未扩大。P0-05上一版本来源问题独立保留，本批不据正常发布宣称已修复。部署原回执/前后基线/完整journal/六组件及真实进程证明在root私有 `deploy/artifacts/container-acl-deploy-633a2ca/`；这不是新一轮全量后端或CI成功声明。

## 九场景真实常规页面与逐轮实盘证明

只有一个准确随机、非root1000:1000、无网络、只读根、唯一自有HDD bind的容器。源有acl-file.bin、acl-directory及其existing.bin；两文件均2097159字节、原UID/GID2100:3100、原模式0640/目录0750、固定mtime与普通user.whaledeck-acl属性。浏览器正常可见Authentik登录、真实Vue填写完整条目、预检、明确确认或取消；不注入Cookie、替换响应或用SQL/缓存改状态。

每轮浏览器写结果后暂停，父进程完成完整内容/inode九字段/两类ACL/原属主/mtime/普通属性、SQL/Job/审计/原Root及attest HDD核对并发出独立信号，才允许下一轮。页面模式组位取mask，实际named/group_obj有效权限另示；不把原文件属主2100:3100、容器执行身份1000:1000或named ID0混为一谈。

| 场景 | 明确目标 | 实际结果 |
| --- | --- | --- |
| access | user2201权限6/group_obj0/mask4/other0，owner6 | Succeeded，access扩展ACL留存，模式0640 |
| noop | 与前一access相同 | Succeeded，NoChange，整份源清单九字段完全不变 |
| mask | 相同named项，mask改6 | Succeeded，模式0660，另一类ACL保持 |
| root | user ID0、权限0，mask6 | Succeeded，显式0保留，原UID/GID仍2100:3100 |
| base | 基础access ACL6/4/0，无named/mask | Succeeded，模式0640，清除该类named项 |
| default | 目录显式named user2201/mask4 | Succeeded，目录access/0750和既有子文件保持 |
| default-noop | 与前一default相同 | Succeeded，NoChange，整份源清单九字段完全不变 |
| clear-default | 明确勾选default空数组 | Succeeded，无默认ACL，目录access/模式/子项保持 |
| cancel | Ready后先继续等待，再明确请求取消 | Canceled，无执行Job、Root开始/回执、HDD或ACL写入，整份源不变 |

实际9次登记重试、8次确认重试只解析原ID/执行ID；刷新接续原引用，浏览器实际登记POST9、确认POST8、文件内容PUT0。最终9意图为8成功/1预检取消，17唯一Job和原Subject的35条ACL审计，未记录路径或planHash等私有内容。原Root状态/计划、回执字段与HDD摘要逐项对账；实际API读取时由产品服务校验回执独立MAC，观察器保留其摘要但未另读密钥重算MAC。8份 `ContainerAclMetadata` 原件永久保留，计划绑定原access/default ACL、实例、原inode/属性、目标模式及NoChange。Root/HDD未参加清理。

源与HDD实际device均2049，元数据备份不是文件内容备份，也不计异盘灾备或NVMe运行覆盖。真实进行中故障及完整权限恢复不能由这九例代替。

## 身份、页面与准确清理

同一管理员上下文从可见用户菜单正常退出：真实顶层logout POST302、供应商end-session导航、BFF401、Authentik401/403及最终/login均已核实。退出不是撤权或删除账号伪装；temporary_user只在正常退出/核验后准确删除本轮身份。原活容器仍在时，匿名4路GET实际401、临时普通用户4读+4写实际403；管理员缺CSRF400、错误Origin403、缺permissions字段400/稳定脱敏码通过。

9张真实PNG及SHA/尺寸保留：1440/320明暗，取消为**1440×600**；已目视核对320暗色显式ID0及1440×600取消。320×600是前批生产预览的通过范围，本批未冒称有真实320×600取消截图。原/目标ACL、mask有效权限和原/执行身份清楚，正文独立滚动，确认/取消按钮在视口内，无横向溢出。

源清理前再次重读全部完整文件/九字段/ACL/xattr并与最后清单相等；只删除本轮准确文件，目录删除前核对仍为同device/inode/模式/属主/link-count、空成员与两类ACL。删除自有子项会自然改变目录时间戳，单列核对后rmdir；随后fixture只允许原original.bin仍为原inode/完整确定内容。准确CID/源目录被移除，未证明数据保留分支未触发，没有递归或通配删除生产目录。

另一root只读进程重新查询原SQL、逐项重算计划/历史、按字节比对原Root及attest HDD、原9组父子信号与来源提交；准确容器/目录和临时身份均不存在，11核心跨门禁不变且healthy，所有任务/终端/锁/各容器意图/屏障/租约0。`independent-final.json`为Passed、cases9/hddRecords8、original-context正常退出true；不声称清理后重读了已删除的源字节。

## 测试、失败与证据边界

新增6项离线证明/准确清理测试（正确清理只留原件、内容不符/未知子项/根symlink/错误父目录在删除前拒绝、显式0/规范mask/default清空配置）加8范围，共14通过；Python -W error及Node语法通过，并接CI。它们不代替上述实际门禁。静态复核后补了回执字段逐项绑定、21依赖来源/安装模块来源与目标版本先决检查，实际门禁源0668215已包括这些加固。

**本次真实发布、九场景主门禁及独立核验均首轮通过**，无失败后只重跑片段、无重复已完成ACL写入。资料定位中有一处不存在的历史HOST_RUNTIME.md读取失败，随后按实际文件定位，无产品动作。前批4272 Unit/432 Integration（7默认原生跳过）、972前端Unit、43 ACL+156关联生产预览是633a2ca候选证据，本轮没有冒称全部重跑或远端CI已全绿。

真实证据为root私有0700目录 `deploy/artifacts/container-acl-browser-b96dc288f1e84cceae6e0befe52c3ad9/`，JSON/日志0600，含baseline/results/逐案SQL-Root-HDD/逐轮信号/9 PNG/browser及verified/independent/final-summary/SHA。凭据仅在内存/子进程环境，不进Git或日志，不截图密码表单。日志 `/tmp/wd-acl-browser-real-first.log`、`/tmp/wd-acl-browser-independent.log`、`/tmp/wd-acl-deploy-verify.log`与证据目录原件保留。

下一步按新独占资源补实际进行中取消/退出、实时组撤权/停用、Worker与Agent重启、回执丢失/Unknown及共享写入延期/明确处置等矩阵，继续ACL递归/历史恢复、可恢复删除/长期清理、P0-05与其余59项/UI/C#酒馆/供应商/AI Live2D。保持核心保护、内部隔离、稳定维护入口和USB/动态网卡，user开发sudo按授权留待用户自行撤销。

文档终检：226份Markdown、1383处相对链接无缺失，旧UI候选正文按字节保留，目标第1节和59项/17移出口径不变，diff检查通过。

项目图谱事件 `whale-deck.container-acl.633a2ca.real-ui` 已取得projectId/key/eventId一致的成功回执并移入synced；阶段保持active，未按常规子范围签收冒称完整ACL或全项目完成。
