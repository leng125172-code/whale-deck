# 回收与恢复任务页链接：修复发布与真实页面复验

2026-11-06。生产已标准覆盖到 `093f44a9c2b97cb164f655d65280fbafcd899ca6`，真实身份的8个任务页场景通过。之前150adfd的真实ElLink失败与预构建记录保留，见[首轮发布和修复](CONTAINER_FILE_RECYCLING_RESTORE_FOOTER_BOOTSTRAP.md)、[预构建恢复入口](CONTAINER_FILE_RECYCLING_RESTORE_RETURNED_OUTCOMES.md)。

## 准确发布与生产核验

CI `42135680358` 与目标headSha一致，固定12任务全部completed/success。更新unit `whaledeck-dev-recycle-restore-493386e5916c`、Invocation `ab58495ccf80478d855087bfba21ea3c`，exited/Result success/ExecMainCode 1/ExecMainStatus 0。没有重跑已完成的预构建，也没有自动回滚。

六组件实际二进制/镜像与HTTP来源匹配093f44a9；Agent PID2974845、Maintenance PID2974836，实际exe与DLL映射对应安装文件。可信账本current为093f44a9、previous为150adfd、pending为空；宿主安装验证通过。

生产迁移1、CHECK19、围栏14、guard1、恢复意图15、活动0。发布及只读页面复验前后，旧29回收/15恢复完整行SHA、Root/HDD内容和2346保护路径保持；保护内容仅允许发布账本变化，安装身份元数据只在既定5路径变化。8数据库平台容器保持原身份，3应用容器正常替换。实际HTTP的3份前端资源与Gateway文件逐字节相等，3个匿名恢复GET返回401，业务写入0。

观测unit `whaledeck-qa-restore-release-observe-ebf73f47561c` / Invocation `a5598e7b08dd42a59ec9d7c8c2c97bf9` 成功；约134.444秒/68个两秒样本全200。采样不证明瞬时零中断，也不补签其他维护连续性场景。

## 全新真实身份页面

真实临时Authentik管理员通过可见登录进入实际Jobs页，读取现有回收、回收计划、独立恢复、独立恢复计划四类任务，各在桌面明色与320宽暗色查看详情，共8场景。详情HTTP响应的ID/类型与所选任务逐项核对，新链接具有link角色及/files目标，新提示存在、递归ACL旧提示和通用重试按钮缺席；根页面无水平溢出，实际dark类与主题一致，8份真实PNG保留并抽看小屏暗色恢复页。

脚本先前把任务ID作为唯一文本定位，但计划意图ID也会出现在结果摘要中；本批在运行前改为核对精确详情响应、再检查抽屉包含该ID。新旧脚本均由不可变预构建检查点和当前UI源码SHA分别保留，未伪造唯一定位通过。

管理业务写入和retry请求均0。完成正常BFF退出POST及OIDC end-session导航，BFF me401、身份源me401/403，最终登录页成立；匿名Jobs GET401。临时账号上下文核对删除，前后完整基线与旧行SHA保持。这里读取历史任务，没有创建新的文件恢复意图；实际页面没有点击/files导航，构建预览的点击回归与本次真实链接角色/href证据分开记载。

## 封存与剩余范围

私有证据 `deploy/artifacts/container-recycle-footer-bootstrap-release-45e7b91538444d94b9efaa22aec6d9a7`，最终74文件清单摘要 `2064ccad36b89b90afdaf51a6e842ac571ba13decd99dea1e5943493638f4795`，逐文件复核通过；父目录0700、文件root:root/0600。旧发布失败74文件摘要7ea18b02…继续保留，未改写其published-ui-failed结论。

本批只签093f44a9标准覆盖、生产核验与真实任务页提示/链接/重试限制，后续写后撤权实机见[独立记录](CONTAINER_FILE_RECYCLING_RESTORE_POSTRETURN_REVOCATION.md)。Superseded/Unknown等剩余组合、跨写入、长期清理与其余59项目标继续；17项移出不算完成，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持，可部署master尚未交付。
