# 容器移动：a59ec97开发覆盖与运行来源

> 最新：真实身份及常规四组移动/Ready取消、普通拒绝/双端退出与准确清理已通过，见[实机页面](CONTAINER_MOVE_REAL_UI.md)。下方“尚未验收”是发布阶段边界；进行中与故障矩阵仍待。

2026-10-29，冻结 `a59ec973478471745a1b7a5aa41da16dae20d426` 已正常开发覆盖。真实移动人类身份/BFF/完整磁盘及页面整链尚未签收，不能把本次发布签成完整文件功能或全部59项完成。

## 发布与运行核对

- 发布前两个dev工作副本来源一致且干净；活动任务、终端、宿主路径锁、上传/目录/复制发布屏障与复制租约均0。
- 原systemd发布单元 `whaledeck-dev-publish-a59ec97.service`，InvocationID `68f431f672304939b3cdd7f2d4206a96`。连续观察原MainPID运行至结束，没有因观察时限重开或自动回滚。
- 该临时单元已GC，`systemctl show` 的not-found及默认Result=success/ExecMainStatus=0本身**不作为成功证据**。使用同一InvocationID的journal `Deactivated successfully`、2分39.920秒结束记录，以及发布后的实际组件来源和健康验证确认成功。
- Agent、MaintenanceHost、API、Worker、Gateway、Migrator实际build-info和镜像revision标签均为a59ec97；HTTP API与Gateway构建信息一致。额外核验两个宿主MainPID的`/proc/.../exe`与已安装文件device/inode吻合，且托管程序集映射inode也是当前安装文件，避免只运行磁盘上的新build-info却误认旧进程。
- 宿主校验通过UDS、固定helper、权限、HDD固定策略及动态发现入口。两个宿主组件的10.0.13运行库/PKCS检查通过；另从运行API/Worker及隔离Migrator镜像直接读取运行库与deps，三者也均为10.0.13（Gateway为Nginx，不套用.NET版本），未增加Agent TCP或API/Worker Docker Socket。
- 迁移 `20261029080510_ContainerMoveIntents` 已实际入库，`container_move_intents`存在；当前移动活动任务/屏障/租约0。
- 11核心容器全部healthy，既有database-platform容器ID/镜像/运行状态/StartedAt与发布前一致；仅按开发发布替换WD组件。
- 经实际Gateway请求两个新增管理员GET入口均匿名401。这不是普通用户/管理员登录或真正移动成功的证据。

## 前序测试与边界

- 3189 Unit/209 Integration通过，7默认原生依赖跳过；冻结73fa4dd真实隔离PG1执行/1通过/0跳过及独立清理通过，见[SQL证据](CONTAINER_MOVE_PERSISTENCE.md)。
- 655前端单元、类型/lint/格式/生产构建通过；生产预览78/78与两宽明暗目视复核通过，见[页面证据](CONTAINER_MOVE_WEB_UI.md)。
- 前序安装态移动签名UDS/复制回归及固定helper三次跨盘通过，见[Agent证据](CONTAINER_MOVE_AGENT_RPC.md)。它们不替代a59ec97当前业务和网页实机验收。
- 最后一个独立进程映射观察器起初错误使用Path.with_suffix，把含点的可执行文件名改成WhaleDeck.dll；改为原名称追加.dll后重验。未改动产品文件或服务。
- 运行依赖观察器初轮错误要求API应用deps也直接列出PKCS包。实测API从ASP.NET共享框架10.0.13加载PKCS（框架deps中的文件版本10.0.1326.50504），且不存在应用目录覆盖DLL；Worker/Migrator应用deps则明确列包10.0.13。已分别核验真实来源，不能把固定assemblyVersion=10.0.0.0当成NuGet包10.0.0。未修改产品依赖或镜像来满足观察器。

私有证据：`deploy/artifacts/container-move-deploy-a59ec97/{before,verified,provenance}.json`；发布日志在root私有平台维护目录，宿主及来源验证日志为`/tmp/wd-container-move-{postdeploy-host,provenance}.log`。GitHub a59ec97 CI仍在运行，未签最终全绿。

## 下一步

真实Authentik管理员登录后，从当前页面/HTTP创建明确移动计划与原执行任务；独立SQL、Root原件、完整内容/普通属性及准确清理核对。补普通用户与匿名拒绝、计划取消、进行中取消/撤权、Worker/Agent重启、Unknown处置等故障与桌面/手机证据，其他59项继续。17项移出不计完成，master未正式交付。
