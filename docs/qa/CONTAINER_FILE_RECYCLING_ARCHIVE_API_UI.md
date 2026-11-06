# 归档复制来源目录、管理员API与Vue候选

2026-11-07，fd2c4243接续[真实PG持久化分项](CONTAINER_FILE_RECYCLING_ARCHIVE_PERSISTENCE.md)，新增来源目录、严格管理员API、归档复制与异常处置页面；0f7e8689仅加严前端独立读取来源ID互异检查。真实隔离PG来源目录已通过，临时资源清理确认；生产未迁移/部署，没有签真实Authentik身份、Agent/HDD业务页面整链，Jobs结果摘要仍待补齐。

## 来源、传输与页面

来源按成功清理意图ID选择，并关联准确原回收行；当前管理员/登录在查询前后复核。目录过滤未完成、Unknown或已成功消费的归档来源，UUID游标与有界分页；服务对两行完整MAC、proof和历史事实重新验证，只输出允许的摘要。HTTP不接受客户端计划、owner、实例或物理路径。

所有API为Administrator/no-store，接既有BFF、Origin/CSRF与资源授权；登记、按键查询、状态、单独确认/取消以及跨管理员Unknown处置均独立。请求converter和query filter拒绝缺失/重复/多余/非规范/类型强制转换字段，确认绑定原意图的规范幂等键。规划/执行保持原任务，响应丢失不会授权新键或自动重复确认。

API仅把对浏览器的published identity设备号/inode/link count和纳秒时间投影为Invariant十进制字符串，内部Root/签名/持久化DTO不变。前端用canonical decimal及BigInt边界校验，避免JS Number精度损失；历史Completed即使当前绑定false也保留复制事实，单独显示当前绑定变化。Unknown保持archiveCopied=null，无发布identity，不提供重复复制或普通取消。

新增容器文件面板和Files异常入口，沿用Element Plus主题/滚动body/固定footer，和现有写入及历史恢复互锁；会话提示只存subject/container/key/source ID/id，最多4个，无确认令牌或路径。退出/换subject/撤权清空提示和UI，AbortController与代际隔离拒绝晚到响应。异步确认失败先查询同一任务，不自动重发；明确处置沿原执行，不能当复制成功。

## 已有检查与失败事实

后端Release构建零警告/错误，5883 Unit零跳过、818默认Integration通过/7默认原生跳过；8范围/14前端CI合同通过。前端类型/lint/构建、71文件1440单元通过；8新开发预览、26生产构建预览（归档8+清理8+恢复10）、前端ID加严后8新生产预览通过。320/1440明暗截图及页尾可见、无横向溢出、独立确认、丢登记刷新、退出晚到、Unknown及管理员处置已按隔离协议夹具检查；它们不是实际登录或HDD证据。

准备期来源catalog参数名CA1725、6项移植测试中的旧文案/Aborted仍带Completed字段、两个lint缺少toThrow消息均已修正，未关闭分析器或放宽回执。首次全量前端1438项中16项因本机Node26实验性localStorage与jsdom冲突失败，使用单进程NODE_OPTIONS=--no-experimental-webstorage后最终1440通过，未改产品业务或全局Node配置。原始已记录日志与修复事实保留，未保存原始工具输出只记事实，不补造。

## 真实来源目录与保持

原生unit `whaledeck-qa-archive-api-pg-fd2c4243`，Invocation `f9734d3693ba48d3b1bd0ddbbbb77f75`，准确源码 `fd2c4243892e62ae7f7180ca46d2f6328dba090b`，exit0/success、412.504秒；实际宏1执行1通过、104阶段标记和cleanupConfirmed=true，外部投递0。新来源目录标记明确执行，已验证真实PG UUID跨页不重不漏、活动归档排除、取消释放和成功复制消费后排除，并对返回的清理/原回收成对事实调用完整来源校验；原持久化双来源锁/围栏/租约/Unknown/事务回滚回归同时通过。当前管理员/登录和HTTP交互仍由类型化替身/协议夹具验证，不冒充真实身份。

后续0f7e8689只加严前端两来源ID碰撞拒绝并补两测试；所有后端、C#测试与原生validator源码逐字节等于fd2c4243。前端最终24项新专项包含在1440全量中，最终生产构建及8预览已重验；不会把新UI提交称为另跑过原生PG。

原生运行中及结束后重新读取生产53回收/29恢复/17清理全行SHA256一致、活动0，归档生产表仍无；Agent/Maintenance仍为PID1244695/1244692，active/NRestarts0。此证据不替代全Root/HDD/维护连续性复验，本批未安装或覆盖部署。

私有证据 `deploy/artifacts/container-recycle-archive-api-ui-251ce1f51f604888831c42451e224e29` 共83文件（另加manifest），含fd2c4243/0f7e8689各25源码、2份实际准备期源码、前后端日志/构建/原生终态、生产保持及4截图。清单SHA256 `1744a99ce1902e20006b44184e5a67b39daf9759e3fd494e3b65a6195f245e18`；root:root目录0700、文件0600。未将私有证据、凭据或内部端点上传项目图谱。

## 续跑点

继续Jobs摘要、准确CI/同版本标准部署、真实身份任务和文件/HDD整链、适用故障矩阵、claim/暂存清理/重复来源恢复策略、留存与维护连续性及其余59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
