# 独立恢复写后实时撤权：验收工具候选

2026-11-06。本批新增原件已返回后的移组、账号停用两场景验收工具，尚未执行这两个实机场景。页尾修复的预构建不重做，准确CI及标准发布仍按[发布检查点](CONTAINER_FILE_RECYCLING_RESTORE_FOOTER_PREBUILD.md)继续。

## 候选约束

`validate_container_recycle_restore_postreturn_revocation.py/.mjs` 仅接受 group-removal/account-disable。全新自有隔离容器和临时真实身份在页面创建来源及独立恢复；必须先冻结准确的写后helper，复核容器root、三内核FD、锁、Invocation及当前Root/claim/HDD，确认来源已Restored、原inode已返回且自身最终日志仍Applying，才允许变更本轮所有者的组或停用状态。执行授权需仍余10分钟以上，避免把自然到期误签为撤权；变更确认前后原helper必须仍活，用原pidfd观察退出。

期望同一次恢复最终Succeeded/Completed、来源已恢复、本操作归属true、成功claim保留；取消时间为空、取消审计和取消POST为0。不能套用写前Canceled/Aborted门禁。另一真实管理员只读原结果和17字段Job摘要；原会话须显示403或登录页并清空两类私有提示，观察者正常BFF/OIDC双端退出。普通9/匿名5拒绝沿用原门禁。

独立 verifier 重新核对被冻结的原件/Root/claim/HDD证据、当前终态、实际返回inode及完整旧业务记录。只有独立live核验成功后才精确清理本轮返回文件、空私有目录和自有容器，历史保留；错过切点不重放，未确定的资源保持留存。

## 已执行与未执行

新增7项负例和证明测试，包含跨操作Job、写前终态、假恢复归属、HDD缺证、取消审计、替代退出/取消、错误身份与近到期。连同原撤权、写后取消/Worker/退出、returned-cut真实临时文件内核检查及摘要检查，共63项以Linux root执行，全通过、零跳过。Python和Node语法检查通过，另有只读静态复核，未发现DTO/文案错配。纯投影和身份提供者单测有明确替身；returned-cut使用隔离临时原件，不是生产实时撤权。

本轮只读复核生产仍为3d80d17，2346保护文件、11核心容器、29回收/15恢复完整行与全部原Root/HDD保持，活动0。没有创建验收身份、执行新业务恢复、发送生产信号或改变账号授权。

私有候选证据 `deploy/artifacts/container-recycle-restore-postreturn-revocation-candidate-08932dd72d704fbb9273e9da6fcbc87a` 保存4份源码与结果，5文件清单摘要 `79dbe3c7c5ee3c8f58163b62e2612b8ae2fc2d2ac88ae938157979174cde91e4`。父目录0700，文件root:root/0600，实际逐文件校验通过。

下一步先完成准确CI、标准页尾发布和真实任务页复验，再用已提交冻结工具分别运行两轮全新资源的写后撤权与独立核验。Unknown、跨写入、长期清理和其余59项目标继续，17项移出不算完成；整项0/59、阶段0/6及酒馆/看板娘功能0%保持。
