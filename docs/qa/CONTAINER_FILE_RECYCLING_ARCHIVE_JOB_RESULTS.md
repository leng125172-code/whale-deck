# 归档复制Jobs安全摘要与同版本预构建

2026-11-07，产品`f318d9ab130ecab94668acf9754a6a6e448de92c`接续[API/Vue候选](CONTAINER_FILE_RECYCLING_ARCHIVE_API_UI.md)，新增19字段历史摘要、数据库事务保存和Jobs界面；QA提交`21cdeb0e191cc78ca8d21e9053c040f97542790d`只更新实机文案断言和导航覆盖，全部产品改动文件逐字节不变。实际PG和预览已通过、四镜像/两宿主预构建已验证；封存时产品准确CI42277124968仍in_progress，不把局部成功当全绿，未安装、生产迁移或覆盖部署。

## 19字段与历史事实

摘要只输出schemaVersion、归档意图/Job、来源清理意图/执行、原回收意图/执行、阶段/状态/码、回执状态、预留入口/预留证明/复制入口、nullable复制归因/来源保护/持有保护/当前绑定及写入保护。固定字段与状态约束重序列化；拒绝缺失/重复/多余/错误类型、来源域/执行域错配。原回收意图从MAC有效的清理记录取得，分别核对回收/清理/本次执行的ID域；没有从原操作ID反推虚构意图。

不包含路径、实例、容器ID、身份/登录/trace、owner、请求/来源/计划/执行/回执MAC、HDD摘要、完整命令/计划/Root回执或已发布物理身份。OperationService对归档两种Job类型只走白名单投影；损坏或旧空值不退回通用成功ResultJson。

规划完成、Reserving/Reserved/Applying、Unknown、终态和ACK均在原事务中保存结果。规划Ready快照不被后续确认/执行/取消覆写；预留或入口不冒充已经复制。Completed允许当前绑定false且保留历史复制true；Unknown归因null，管理员处置可保存同ID晚到证明但不把失败的处置Job改成Succeeded。Jobs页分别展示这些历史事实并禁止Unknown的普通取消/通用重试。

旧清理确认与结果提示同步改为：原件删除不可撤销，恢复须走独立归档复制入口并重新核验HDD备份。九份实机清理门禁仍要求同一规范提示、来源/身份/写入/HDD/审计等原断言，未放宽确认条件；仅语法及453关联root检查验证，未声称重跑这些真实身份故障组合。

## 测试与真实PG

产品构建零警告错误；5912 Unit零跳过、818默认Integration通过/7默认原生跳过，最终72前端文件1469项通过，类型/lint/生产构建、8范围/14前端CI合同通过。新Jobs27项组件/解析检查、C#投影/快照29专项通过；4新开发浏览器场景、16生产构建场景（新摘要4/旧清理摘要4/清理操作8）、追加8导航/ACL生产预览通过，包含320/1440明暗、16种历史/缺失/损坏结果、零写入/零重试、Unknown取消缺席与恢复链接。页面/API仍是隔离协议夹具，不是实际身份/HDD业务验收。

实际PGunit `whaledeck-qa-archive-jobs-pg-f318d9ab`，Invocation `08e990e3f82343f99844323ac43025cd`，exit0/success、410.708秒；标准状态1执行1通过、105阶段、新19字段投影标记和临时资源清理确认，外部投递0。真实Job持久值/OperationService投影/19字段/来源/nullable绑定/阶段标记逐项匹配；规划快照冻结，Unknown/ACK、丢回执后晚到Completed、成功且绑定false、预检失败及审计故障回滚后仍Applying的原摘要均核对。原双来源锁/围栏/租约/目录回归同时通过。

最初4个新组件断言仍使用清理/删除文案，23通过4失败，修正文案后未改变事实校验。原生测试循环新建JsonSerializerOptions触发CA1869，改用缓存Web选项，不禁用分析器；原始未保存工具输出只保留事实，未补造。已保存实际准备期前端源码。Git直连推送曾GnuTLS失败，有限重试已成功；未改全局代理或凭据配置。

## 标准预构建与生产保持

仓库用户建立独立detached worktree `deploy/artifacts/archive-jobs-build-f318d9ab`。私有环境仅以受限外部链接供标准Compose插值，未复制到主工作区或进入Git archive上下文；运行checkout仍9694af1c。标准build-images.sh冻结公开源、五基础镜像固定digest，unit `whaledeck-qa-archive-jobs-images-f318d9ab` / Invocation `945c5f71930d452fad4a602a58366651` success，471.797秒。API/Worker/Migrator/Gateway四OCI标签和无网络/只读/无capability的独立build-info均精确为f318d9ab。

标准publish.sh同一源码自包含linux-x64，unit `whaledeck-qa-archive-jobs-hosts-f318d9ab` / Invocation `c9ad842d480641ea9da47c5edd0cc8f8` success，164.253秒。707文件checksum逐个通过，标准运行时servicing检查及两apphost build-info对应f318d9ab；未执行install或migrator。重定向host日志头不含Invocation，使用实际journal JSON核对，没有补造ID或重跑构建。

本地候选镜像标签指向新构建，三个运行应用的实际容器/镜像ID、PID、startedAt完全保持；Agent/Maintenance PID及active/NRestarts、运行checkout完全保持。生产53回收/29恢复/17清理完整行SHA256和活动0保持，归档生产表仍不存在。此保持不冒充全Root/HDD/维护入口连续性复验。

## 封存与续跑

私有证据 `deploy/artifacts/container-recycle-archive-jobs-9e6b24f6c88a413abd79cf2437465b1c` 共91文件（另加manifest），含19产品/30最终源码、4截图、实际原生/前后端/构建/运行身份与707文件校验清单；原宿主产物另保留在 `deploy/artifacts/archive-jobs-host-publish-f318d9ab`，未重建或安装。清单SHA256 `f6d675f02ef3082233a9ad5e32db8e05ea8354adbd8586f30de3ea0b3d086d2a`，root:root父目录0700/文件0600。私有prebuild-reference/results记载已完成任务，不重放构建。

下一步核产品准确CI全部终态及最新生产预检；仅在标准工具接受目标及一致产物后覆盖部署，核生产迁移/六组件/保持，再做真实身份、任务页与当前文件/HDD独立重读整链和适用故障矩阵。若目标或源码改变，按标准流程重新同版本构建，不伪改标签或用旧绿灯。claim/暂存清理、重复来源策略、留存、维护连续性与其余59项继续，17项移出不算完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
