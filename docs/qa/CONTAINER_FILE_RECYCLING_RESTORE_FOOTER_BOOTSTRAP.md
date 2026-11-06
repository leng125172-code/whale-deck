# 任务页尾首轮发布、真实失败与组件注册修复

2026-11-06。本批完成150adfd标准发布和生产核验，真实任务页发现链接未注册，尚未签收页面。新增修复候选及构建预览回归通过，仍需新准确CI、标准覆盖和全新真实身份复验。59项活动范围及17项移出边界保持。

## 首轮标准发布与生产核验

准确提交 `150adfd2681f7b0a53b07c1d2f27bcb1f8721bbe` 的CI `42131426329` 最终12项全部completed/success，包含6个完整浏览器分片及frontend汇总。仓库用户先前已将运行目录固定到该版本，私有配置保留；没有重做已完成的预构建。

标准更新 unit `whaledeck-dev-recycle-restore-c5bd815fe631`、Invocation `b9bf982e6b0247fdb3711fdb46261420`，exited/Result success/ExecMainCode 1/ExecMainStatus 0。六组件实际二进制或镜像及HTTP来源匹配150adfd；Agent PID2883670、Maintenance PID2883664，实际exe与DLL映射对应安装文件。账本current为150adfd、previous为3d80d17、pending为空；没有自动回滚。

生产迁移1、CHECK19、围栏14、guard1、恢复意图15、活动0。旧29回收/15恢复完整PostgreSQL行SHA、Root/HDD内容和2346保护路径保持；除可信发布账本外保护文件内容保持，安装身份元数据只按既定5路径边界变动；8个数据库平台容器身份保持，3个应用容器正常替换。宿主安装校验通过。3个前端HTTP实际资源与Gateway文件逐字节一致，3个匿名恢复GET均401，业务写入0。

独立维护观测 unit `whaledeck-qa-restore-release-observe-d83b5be63245`、Invocation `3f95d60e43894a4aa159f718051ddae4` 成功，约126.444秒/64个两秒样本全部200。不据此声称瞬时零中断或补签其他维护连续性场景。

## 真实页面失败与原因

真实临时Authentik管理员经正常登录进入实际Jobs页，打开现存回收任务详情。新提示文字已出现，但“前往文件页核对原回收或恢复任务”没有链接角色，首个桌面明色场景失败，完成场景0；管理写入/retry请求0。原截图、错误、脚本及失败后完整业务/保护基线核对均保留，账号上下文执行清理后返回原页面失败异常。不能记8项真实页面通过，也不能将这次失败当成正常双端退出验收。

原因是生产入口 `web/src/main.ts` 按需注册Element Plus组件时漏了ElLink，Vue把el-link留作未解析元素；文字可见却不是可点击的a链接。JobsView单元测试加载完整ElementPlus插件，因此先前25项通过没有覆盖生产注册差异。修复仅在生产入口导入并注册ElLink，沿用原取消/重试安全判定及两类提示分流。

## 候选回归与边界

在已有acl-job-results生产预览套件增加4个屏宽/主题组合，每组遍历回收、回收计划、独立恢复、独立恢复计划及4类递归ACL任务。断言实际链接角色、a标签及/files目标、两类提示隔离、无通用重试按钮/请求与管理写入，实际点击导航，检查真实主题和无横向溢出。使用真实生产bootstrap，不在测试中注入完整组件插件；身份/API为明确夹具，不算生产身份整链。

同一新增回归先在未修复产物复现：首项失败、其余3项未运行。修复后完整套件8项全通过，耗时50.9秒；其中4个新组合覆盖32次任务链接核对，另4个为原ACL历史摘要回归。另25任务页单元、类型检查、oxlint/eslint、格式及生产构建、9分片契约/8范围检查通过。

首轮发布私有证据沿用 `deploy/artifacts/container-recycle-footer-release-f491976c66ad4576bc6a1816fc5ffac6`，最终74文件清单 `RELEASE_EVIDENCE_SHA256.json` 摘要 `7ea18b024a04af24a5153990f09a136a1e1eabcaa1996ca75b32a6fe1c28cd84`，request明确为published-ui-failed。原预构建17文件检查点保持，不覆盖其历史结论。

注册修复候选证据 `deploy/artifacts/container-recycle-footer-bootstrap-candidate-fe216ad214124541be4c1bb077215feb`，6文件清单摘要 `19c13f727b0ad8554a87276d5c02161721bb07cf7d91ae869c71ea467569741c`。两份清单均实际逐文件复核，父目录0700、文件root:root/0600。私有配置和凭据不入Git或项目图谱。

生产仍为150adfd，ElLink注册修复尚未部署。下一步准确新CI全绿后标准覆盖，以全新真实身份复验8个任务页场景，再运行已提交的写后移组/停用工具、Unknown、长期清理及其余目标。整项0/59、阶段0/6，酒馆/供应商P4-08及Live2D看板娘P5-05功能0%保持；整体25%–35%只是粗估，可部署master尚未交付。
