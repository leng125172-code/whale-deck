# 实际发布保护、中断旧版本捕获与跨管理员接管

2026-10-27，在原部署 fab6e2e 上完成；产品及核心容器未更新或重启。`validate_container_upload_publish_interruption.py` 最终完整运行退出0，通过真实 Authentik/BFF、SQL、Worker、Agent、Root 和物理 HDD；未人工修改数据库、缓存、Root journal 或时钟。

## 触发点和持久保护

第一个独占普通容器中准备512 MiB旧文件，固定完整内容摘要和原inode/权限。以真实管理员计划、确认并完整暂存2 MiB+7新内容；等待实际 SQL Publishing、RootStartedAtUtc 非空、PublishBarrier=true，Root Staged 已保存 OriginalCapture，HDD Preparing 及非空未完成 partial。

观察到旧版本副本已写98,304字节后，仅停止该测试容器。Root 捕获随后因原实例失效停止，最终留存71,630,848字节的未完成 HDD partial 与 Preparing 元数据。原512 MiB全内容及inode/权限不变，未进入目标文件原子替换；Root新内容暂存载荷仍完整保留。此处是实际 Publish 调用内部的旧版本捕获中断，尚未进入 PublishApplying；不能据此签原子交换期间强杀或丢回执组合。

Worker记录原任务Unknown、RootStarted保持、全局PublishBarrier保持，租约释放且没有伪造PublishedVersion。此时通过另一名真实管理员在第二个独占、仍运行的容器创建并暂存另一个上传；独立SQL观察到至少两次Worker领取后推迟，仍Ready/RootStarted空，目标不存在。第二个目标和实例正常，阻止发布的原因是保留的全局保护。

## 原身份删除后的二次确认

原管理员正常双端退出并从Authentik精确删除后，另一主体读取历史、生成持久恢复计划并二次确认。Root永久Seal保留完整原Staged journal、OriginalCapture与原记录摘要；ActorHash绑定新管理员，恢复execution id一致，没有重发Publish或目标字节。

恢复任务Disposed/FILES_RECOVERY_DISPOSED；原任务原子记Failed/CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED并解除PublishBarrier。原不可变身份、原登录绑定、目标/实例、请求、RootStarted和完整载荷保持，未把“确认未知”描述成成功发布。

此前被推迟的第二个上传随后自动Succeeded，独立检查SQL、Root Published和目标完整2 MiB+7字节正确、无虚构旧版本备份。再次核对第一个原件全内容及inode不变、Root新内容载荷保留、HDD partial完整摘要/指纹与Preparing元数据均未被接管修改。两测试容器和源目录精确清理；另起只读检查确认二者源目录已不存在，永久SQL/审计/Root/HDD证据保留。

## 失败与修复

首轮业务步骤已走通，但最终夹具清理失败：新建文件发布也留下一个空的root私有暂存目录，观察器未将其列入预期。修正观察器明确登记并验证该空目录，不删除或放宽未知对象检查；完整第二轮通过。

首轮留下的精确第二源目录另行独立核对：原容器已不存在、唯一SQL成功任务及Root Published、目标全字节/Published pending inode、原合成文件全字节和两文件UID/GID/模式、根私有空目录和精确三个子对象全部匹配，才逐个unlink并rmdir，未按前缀或递归清理。两次清理探针分别因相对路径与Docker错误文本大小写拒绝，在任何删除之前终止；修正后同样检查全部通过。失败日志和精确清理回执保留，不删除HDD未完成捕获或Root载荷来制造干净结果。

最终日志 `/tmp/wd-container-publish-interruption-final.log`；首轮 `/tmp/wd-container-publish-interruption.log`。私有证据在 `deploy/artifacts/container-publish-interruption-*` 的0700目录/0600文件中。最终总计22条永久上传意图：10 Succeeded、7 Canceled、5 Failed；Unknown=0、活动租约=0、PublishBarrier=0，11核心healthy。两小时权限测试仍保持原PID运行，不将其临时身份当作泄漏清理。

本批不是浏览器进行中取消、Worker/Agent重启恢复、PublishApplying原子交换故障或完整历史恢复。源与留存同物理HDD，是版本捕获和故障保留证据，不冒称NVMe/HDD异盘灾难恢复。上述剩余组合与整个59项目标继续active；17项移出不算完成。
