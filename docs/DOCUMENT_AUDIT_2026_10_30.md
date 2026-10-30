# Markdown冲突审计与目标修订（2026-10-30）

审计基线：`bcfe74d`。全量读取并扫描Whale Deck仓库原有183份Markdown（149份QA、34份其他文档，共1836183字节），重点复核当前范围、权限、阶段、AI架构与历史状态。未把兄弟项目或第三方依赖源码当作本仓库目标；字体NOTICE保持原样。

## 已修正的问题

- 可复制目标正文遗漏C#完整酒馆、集中供应商、AI驱动Live2D和验收数量，已完整更新并移至历史日志之前。
- 页面/用例仍把数据库恢复、GitLab标为HOLD；现按P2-08/P4-07实施，保留安全完成前不开放入口的门槛。
- 页面/路线仍有邮件DNS托管、多节点、团队及76项作为当前范围的措辞；现明确59主项、17移出与有限酒馆扩展例外。
- 文件专题仍将企业租户隔离列为当前工作；现保留用户/资源隔离并注明旧批次范围。
- 72份QA补充历史口径，原正文逐字校验保留，不把旧版本、当时下一步、70/76项当当前状态。
- 实施状态新增最后记录版本及范围，旧运行快照和缺口明确标为历史；未重新探测主机或虚构部署。
- 主表两处“普通权限尚未发布”与已记录实机证据冲突，已更新；所有者永久业务/UI仍待，未提前签收。
- 根README/前端指南统一Linux及pnpm固定锁文件；安装文档区分Docker配置失联恢复与禁止dev自动回滚。

## 当前唯一目标与证据规则

以[可复制目标正文和59项主表](GOAL_P0_P5.md)、[范围决策](SCOPE_SINGLE_WORKSTATION.md)、[完整酒馆账本](AI_TAVERN_ACCEPTANCE.md)和[供应商/Live2D契约](AI_PROVIDERS_LIVE2D.md)为准。59主项、272ST、6PET父项与16细分均保留；文档修订不新增功能完成数。历史测试/失败/运行版本只证明原批次，当前功能状态按最新对应版本证据判断。

本轮只做文档审计与一致性校验，未运行模型、安装SDK、改sudo或部署服务。用户已要求更新后自动执行目标；目标正文已保存，目标模式须取得Cindy设置成功回执才算启用，不能以本文代替。

## 原183份文档审阅索引

下表记录审计覆盖；QA为批次证据，不改写其历史测试结论。无范围性命中不等于对技术实现作形式化正确性证明。

| 文档 | 审阅归属 |
| --- | --- |
| [AGENTS.md](../AGENTS.md) | 当前规范/专题或开发说明 |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | 当前规范/专题或开发说明 |
| [README.md](../README.md) | 当前规范/专题或开发说明 |
| [deploy/branding/authentik/README.md](../deploy/branding/authentik/README.md) | 当前规范/专题或开发说明 |
| [deploy/install/README.md](../deploy/install/README.md) | 当前规范/专题或开发说明 |
| [docs/AGENT_RPC.md](AGENT_RPC.md) | 当前规范/专题或开发说明 |
| [docs/AI_PROVIDERS_LIVE2D.md](AI_PROVIDERS_LIVE2D.md) | 当前规范/专题或开发说明 |
| [docs/AI_TAVERN_ACCEPTANCE.md](AI_TAVERN_ACCEPTANCE.md) | 当前规范/专题或开发说明 |
| [docs/AI_TAVERN_MASCOT.md](AI_TAVERN_MASCOT.md) | 当前规范/专题或开发说明 |
| [docs/CONTAINER_FILE_UPLOADS.md](CONTAINER_FILE_UPLOADS.md) | 当前规范/专题或开发说明 |
| [docs/DATA_MODEL.md](DATA_MODEL.md) | 当前规范/专题或开发说明 |
| [docs/DEVELOPMENT_WORKSPACES.md](DEVELOPMENT_WORKSPACES.md) | 当前规范/专题或开发说明 |
| [docs/DIRECTORY_TREE_OPERATIONS.md](DIRECTORY_TREE_OPERATIONS.md) | 当前规范/专题或开发说明 |
| [docs/FEATURES.md](FEATURES.md) | 当前规范/专题或开发说明 |
| [docs/FILE_HISTORY_RECYCLE.md](FILE_HISTORY_RECYCLE.md) | 当前规范/专题或开发说明 |
| [docs/FILE_MUTATIONS.md](FILE_MUTATIONS.md) | 当前规范/专题或开发说明 |
| [docs/FILE_TRANSPORT.md](FILE_TRANSPORT.md) | 当前规范/专题或开发说明 |
| [docs/GOAL_P0_P5.md](GOAL_P0_P5.md) | 当前规范/专题或开发说明 |
| [docs/IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) | 当前规范/专题或开发说明 |
| [docs/NOTIFICATIONS.md](NOTIFICATIONS.md) | 当前规范/专题或开发说明 |
| [docs/NOTIFICATION_DELIVERY.md](NOTIFICATION_DELIVERY.md) | 当前规范/专题或开发说明 |
| [docs/PAGES.md](PAGES.md) | 当前规范/专题或开发说明 |
| [docs/PERMISSIONS.md](PERMISSIONS.md) | 当前规范/专题或开发说明 |
| [docs/README.md](README.md) | 当前规范/专题或开发说明 |
| [docs/ROADMAP.md](ROADMAP.md) | 当前规范/专题或开发说明 |
| [docs/SCOPE_SINGLE_WORKSTATION.md](SCOPE_SINGLE_WORKSTATION.md) | 当前规范/专题或开发说明 |
| [docs/SESSION_TELEMETRY_QA.md](SESSION_TELEMETRY_QA.md) | 当前规范/专题或开发说明 |
| [docs/UI_STYLE_GUIDE.md](UI_STYLE_GUIDE.md) | 当前规范/专题或开发说明 |
| [docs/USE_CASES.md](USE_CASES.md) | 当前规范/专题或开发说明 |
| [docs/qa/API_RATE_LIMITS.md](qa/API_RATE_LIMITS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/APPLICATION_BACKUPS.md](qa/APPLICATION_BACKUPS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/APPLICATION_CONFIGURATION.md](qa/APPLICATION_CONFIGURATION.md) | 批次证据；按记录版本解释 |
| [docs/qa/AUTHENTIK_ADMIN_GLOBAL_UI.md](qa/AUTHENTIK_ADMIN_GLOBAL_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/AUTHENTIK_WALLPAPER_LOGO.md](qa/AUTHENTIK_WALLPAPER_LOGO.md) | 批次证据；历史范围已加说明 |
| [docs/qa/BACKUP_CAPACITY.md](qa/BACKUP_CAPACITY.md) | 批次证据；按记录版本解释 |
| [docs/qa/BFF_LOGIN_LIVENESS.md](qa/BFF_LOGIN_LIVENESS.md) | 批次证据；按记录版本解释 |
| [docs/qa/BOOT_MAINTENANCE_STATUS.md](qa/BOOT_MAINTENANCE_STATUS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/BUILD_PROVENANCE.md](qa/BUILD_PROVENANCE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/CI_FRONTEND_BOOTSTRAP.md](qa/CI_FRONTEND_BOOTSTRAP.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_AGENT_RPC.md](qa/CONTAINER_COPY_AGENT_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_INFLIGHT.md](qa/CONTAINER_COPY_INFLIGHT.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_INTENTS.md](qa/CONTAINER_COPY_INTENTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_PERSISTENCE.md](qa/CONTAINER_COPY_PERSISTENCE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_SCOPE.md](qa/CONTAINER_COPY_SCOPE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_WEB_UI.md](qa/CONTAINER_COPY_WEB_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_COPY_WORKFLOW.md](qa/CONTAINER_COPY_WORKFLOW.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_DETAIL_LAYOUT.md](qa/CONTAINER_DETAIL_LAYOUT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/CONTAINER_DIRECTORIES.md](qa/CONTAINER_DIRECTORIES.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_DIRECTORY_PROCESS_RESTARTS.md](qa/CONTAINER_DIRECTORY_PROCESS_RESTARTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_DIRECTORY_RESTARTS.md](qa/CONTAINER_DIRECTORY_RESTARTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_DIRECTORY_UNKNOWN.md](qa/CONTAINER_DIRECTORY_UNKNOWN.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_AGENT_RPC.md](qa/CONTAINER_FILE_AGENT_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_BFF_UI.md](qa/CONTAINER_FILE_BFF_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_CANCEL_FENCE.md](qa/CONTAINER_FILE_CANCEL_FENCE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_HDD_CAPTURE.md](qa/CONTAINER_FILE_HDD_CAPTURE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_HISTORY_ENGINE.md](qa/CONTAINER_FILE_HISTORY_ENGINE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_PUBLISHING.md](qa/CONTAINER_FILE_PUBLISHING.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_READS.md](qa/CONTAINER_FILE_READS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_READS_INSTALLED.md](qa/CONTAINER_FILE_READS_INSTALLED.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_RESTORATION.md](qa/CONTAINER_FILE_RESTORATION.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_STAGING.md](qa/CONTAINER_FILE_STAGING.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_UPLOAD_INTENTS.md](qa/CONTAINER_FILE_UPLOAD_INTENTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_FILE_WRITE_RPC.md](qa/CONTAINER_FILE_WRITE_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_HISTORY_RPC.md](qa/CONTAINER_HISTORY_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_AGENT_RPC.md](qa/CONTAINER_MOVE_AGENT_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_DEPLOYMENT.md](qa/CONTAINER_MOVE_DEPLOYMENT.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_INFLIGHT.md](qa/CONTAINER_MOVE_INFLIGHT.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_INTENTS.md](qa/CONTAINER_MOVE_INTENTS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/CONTAINER_MOVE_PERSISTENCE.md](qa/CONTAINER_MOVE_PERSISTENCE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_REAL_UI.md](qa/CONTAINER_MOVE_REAL_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_SCOPE.md](qa/CONTAINER_MOVE_SCOPE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_UNKNOWN.md](qa/CONTAINER_MOVE_UNKNOWN.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_WEB_UI.md](qa/CONTAINER_MOVE_WEB_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_MOVE_WORKFLOW.md](qa/CONTAINER_MOVE_WORKFLOW.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_OWNERSHIP_AGENT_RPC.md](qa/CONTAINER_OWNERSHIP_AGENT_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSIONS_ENGINE.md](qa/CONTAINER_PERMISSIONS_ENGINE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSION_AGENT_RPC.md](qa/CONTAINER_PERMISSION_AGENT_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSION_FAULTS.md](qa/CONTAINER_PERMISSION_FAULTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSION_PERSISTENCE.md](qa/CONTAINER_PERMISSION_PERSISTENCE.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSION_REAL_UI.md](qa/CONTAINER_PERMISSION_REAL_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_PERMISSION_WEB_UI.md](qa/CONTAINER_PERMISSION_WEB_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_RELATIONSHIPS.md](qa/CONTAINER_RELATIONSHIPS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_RESTORATION_ATTRIBUTES.md](qa/CONTAINER_RESTORATION_ATTRIBUTES.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_RESTORATION_PUBLICATION.md](qa/CONTAINER_RESTORATION_PUBLICATION.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_RESTORATION_RESTARTS.md](qa/CONTAINER_RESTORATION_RESTARTS.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_RESTORATION_RPC.md](qa/CONTAINER_RESTORATION_RPC.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_TERMINAL_AGENT.md](qa/CONTAINER_TERMINAL_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/CONTAINER_TERMINAL_BFF_UI.md](qa/CONTAINER_TERMINAL_BFF_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/CONTAINER_TEXT_EDITOR.md](qa/CONTAINER_TEXT_EDITOR.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_ADMINISTRATOR_RECOVERY.md](qa/CONTAINER_UPLOAD_ADMINISTRATOR_RECOVERY.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_BFF_UI.md](qa/CONTAINER_UPLOAD_BFF_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_BROWSER_INTERRUPTION.md](qa/CONTAINER_UPLOAD_BROWSER_INTERRUPTION.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_HTTP_WORKER.md](qa/CONTAINER_UPLOAD_HTTP_WORKER.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_INTERRUPTION.md](qa/CONTAINER_UPLOAD_INTERRUPTION.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_PUBLISH_INTERRUPTION.md](qa/CONTAINER_UPLOAD_PUBLISH_INTERRUPTION.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_UPLOAD_RECOVERY_SEAL.md](qa/CONTAINER_UPLOAD_RECOVERY_SEAL.md) | 批次证据；按记录版本解释 |
| [docs/qa/CONTAINER_USAGE.md](qa/CONTAINER_USAGE.md) | 批次证据；按记录版本解释 |
| [docs/qa/DEPENDENCY_SECURITY_2026_10.md](qa/DEPENDENCY_SECURITY_2026_10.md) | 批次证据；按记录版本解释 |
| [docs/qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md](qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md) | 批次证据；历史范围已加说明 |
| [docs/qa/DISPLAY_UNITS.md](qa/DISPLAY_UNITS.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_ARCHIVE_UI.md](qa/FILE_ARCHIVE_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_BROWSER.md](qa/FILE_BROWSER.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_BROWSER_LOCATION.md](qa/FILE_BROWSER_LOCATION.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_CONTENT_SEARCH.md](qa/FILE_CONTENT_SEARCH.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_HIERARCHY_LOCKS.md](qa/FILE_HIERARCHY_LOCKS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_LIST_HEADER_BOUNDARIES.md](qa/FILE_LIST_HEADER_BOUNDARIES.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_MANAGEMENT.md](qa/FILE_MANAGEMENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_MUTATION_WORKFLOW.md](qa/FILE_MUTATION_WORKFLOW.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TASK_RECOVERY.md](qa/FILE_TASK_RECOVERY.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TEXT_HDD.md](qa/FILE_TEXT_HDD.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TRANSPORT_STOP_RACE.md](qa/FILE_TRANSPORT_STOP_RACE.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_ARCHIVES.md](qa/FILE_TREE_ARCHIVES.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_COPY_AGENT.md](qa/FILE_TREE_COPY_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_COPY_UI.md](qa/FILE_TREE_COPY_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_MOVE_AGENT.md](qa/FILE_TREE_MOVE_AGENT.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_MOVE_BFF.md](qa/FILE_TREE_MOVE_BFF.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_MOVE_ENGINE.md](qa/FILE_TREE_MOVE_ENGINE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_MOVE_UI.md](qa/FILE_TREE_MOVE_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_OVERWRITE_ADMINISTRATOR_RECOVERY.md](qa/FILE_TREE_OVERWRITE_ADMINISTRATOR_RECOVERY.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_REAL_UI.md](qa/FILE_TREE_OVERWRITE_REAL_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_RESTORING_AGENT.md](qa/FILE_TREE_OVERWRITE_RESTORING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_RESTORING_ENGINE.md](qa/FILE_TREE_OVERWRITE_RESTORING_ENGINE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md](qa/FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_RESTORING_PERSISTENCE.md](qa/FILE_TREE_OVERWRITE_RESTORING_PERSISTENCE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_OVERWRITE_RESTORING_UI.md](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/FILE_TREE_PACKING.md](qa/FILE_TREE_PACKING.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PACKING_AGENT.md](qa/FILE_TREE_PACKING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PACKING_BFF.md](qa/FILE_TREE_PACKING_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PACKING_UI.md](qa/FILE_TREE_PACKING_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_AGENT.md](qa/FILE_TREE_PURGING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_BFF.md](qa/FILE_TREE_PURGING_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_BUSINESS_MATERIAL.md](qa/FILE_TREE_PURGING_BUSINESS_MATERIAL.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_ENGINE.md](qa/FILE_TREE_PURGING_ENGINE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_HTTP_RECOVERY.md](qa/FILE_TREE_PURGING_HTTP_RECOVERY.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_INSTALLED.md](qa/FILE_TREE_PURGING_INSTALLED.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_POSTGRES.md](qa/FILE_TREE_PURGING_POSTGRES.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_REAL_UI.md](qa/FILE_TREE_PURGING_REAL_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_RECOVERY_SUMMARY.md](qa/FILE_TREE_PURGING_RECOVERY_SUMMARY.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_PURGING_UI.md](qa/FILE_TREE_PURGING_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RECYCLING_AGENT.md](qa/FILE_TREE_RECYCLING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RECYCLING_BFF.md](qa/FILE_TREE_RECYCLING_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RECYCLING_ENGINE.md](qa/FILE_TREE_RECYCLING_ENGINE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RECYCLING_UI.md](qa/FILE_TREE_RECYCLING_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RESTORING_AGENT.md](qa/FILE_TREE_RESTORING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RESTORING_BFF.md](qa/FILE_TREE_RESTORING_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RESTORING_ENGINE.md](qa/FILE_TREE_RESTORING_ENGINE.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_RESTORING_UI.md](qa/FILE_TREE_RESTORING_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_UNPACKING.md](qa/FILE_TREE_UNPACKING.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_UNPACKING_AGENT.md](qa/FILE_TREE_UNPACKING_AGENT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_UNPACKING_BFF.md](qa/FILE_TREE_UNPACKING_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/FILE_TREE_UNPACKING_UI.md](qa/FILE_TREE_UNPACKING_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/HEALTH_ALERTS.md](qa/HEALTH_ALERTS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/HOST_TERMINAL_BFF.md](qa/HOST_TERMINAL_BFF.md) | 批次证据；历史范围已加说明 |
| [docs/qa/HOST_TERMINAL_PTY.md](qa/HOST_TERMINAL_PTY.md) | 批次证据；历史范围已加说明 |
| [docs/qa/HOST_TERMINAL_UI.md](qa/HOST_TERMINAL_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/JOB_EVENT_STREAM.md](qa/JOB_EVENT_STREAM.md) | 批次证据；历史范围已加说明 |
| [docs/qa/JOURNAL_PROCESS_LAYOUT.md](qa/JOURNAL_PROCESS_LAYOUT.md) | 批次证据；历史范围已加说明 |
| [docs/qa/METRICS_HISTORY.md](qa/METRICS_HISTORY.md) | 批次证据；按记录版本解释 |
| [docs/qa/METRIC_AGGREGATION.md](qa/METRIC_AGGREGATION.md) | 批次证据；按记录版本解释 |
| [docs/qa/METRIC_PARTITIONS.md](qa/METRIC_PARTITIONS.md) | 批次证据；按记录版本解释 |
| [docs/qa/MONITORING_ALERTS.md](qa/MONITORING_ALERTS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_EXPLICIT_INTENTS.md](qa/NOTIFICATION_EXPLICIT_INTENTS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_MANAGEMENT.md](qa/NOTIFICATION_MANAGEMENT.md) | 批次证据；按记录版本解释 |
| [docs/qa/NOTIFICATION_PRIVATE_BOUNDARIES.md](qa/NOTIFICATION_PRIVATE_BOUNDARIES.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_PRIVATE_CONFIGURATION.md](qa/NOTIFICATION_PRIVATE_CONFIGURATION.md) | 批次证据；按记录版本解释 |
| [docs/qa/NOTIFICATION_PRIVATE_UI.md](qa/NOTIFICATION_PRIVATE_UI.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_PRIVATE_WORKFLOW.md](qa/NOTIFICATION_PRIVATE_WORKFLOW.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_READ_SNAPSHOTS.md](qa/NOTIFICATION_READ_SNAPSHOTS.md) | 批次证据；历史范围已加说明 |
| [docs/qa/NOTIFICATION_SENDER.md](qa/NOTIFICATION_SENDER.md) | 批次证据；历史范围已加说明 |
| [docs/qa/OIDC_DYNAMIC_LOGOUT.md](qa/OIDC_DYNAMIC_LOGOUT.md) | 批次证据；按记录版本解释 |
| [docs/qa/P0_RECOVERY.md](qa/P0_RECOVERY.md) | 批次证据；历史范围已加说明 |
| [docs/qa/PLAN_REVIEW_20261014.md](qa/PLAN_REVIEW_20261014.md) | 批次证据；历史范围已加说明 |
| [docs/qa/RUNTIME_OWNERSHIP_REPAIR.md](qa/RUNTIME_OWNERSHIP_REPAIR.md) | 批次证据；按记录版本解释 |
| [docs/qa/SEARCH_LAYOUT.md](qa/SEARCH_LAYOUT.md) | 批次证据；按记录版本解释 |
| [docs/qa/SESSION_EXPIRY_UI.md](qa/SESSION_EXPIRY_UI.md) | 批次证据；按记录版本解释 |
| [docs/qa/TELEMETRY_CONTINUITY.md](qa/TELEMETRY_CONTINUITY.md) | 批次证据；按记录版本解释 |
| [docs/qa/TELEMETRY_CURRENT_CARDS.md](qa/TELEMETRY_CURRENT_CARDS.md) | 批次证据；按记录版本解释 |
| [docs/research/AI_MASCOT_SILLYTAVERN_CODEX_DOCKER.md](research/AI_MASCOT_SILLYTAVERN_CODEX_DOCKER.md) | 参考来源/历史研究；服从最新目标 |
| [docs/research/DSH_PET_REFERENCE.md](research/DSH_PET_REFERENCE.md) | 参考来源/历史研究；服从最新目标 |
| [docs/research/SILLYTAVERN_FEATURE_INVENTORY.md](research/SILLYTAVERN_FEATURE_INVENTORY.md) | 参考来源/历史研究；服从最新目标 |
| [web/README.md](../web/README.md) | 当前规范/专题或开发说明 |
| [web/public/fonts/whaledeck-mono/NOTICE.md](../web/public/fonts/whaledeck-mono/NOTICE.md) | 第三方字体声明；未修改 |
