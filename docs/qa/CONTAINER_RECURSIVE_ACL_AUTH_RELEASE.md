# 引用保护覆盖发布、真实取消与认证退出补验

2026-11-04。产品冻结 `b880208f6d9c606fcb1c48aada2843ec1a826526`，承接[七类真实分项及失败原件](CONTAINER_RECURSIVE_ACL_REAL_UI_PARTIAL.md)。**标准覆盖、当次原引用重载和预检取消分项已通过；另两个全新身份上下文的拒绝/正常双端退出补验通过。原取消总脚本仍失败，原普通用户上下文退出未完成，不能改写成单次完整门禁全绿，也不能证明旧丢引用现场的根因。** 完整故障矩阵、递归恢复、删除和 59 主任务继续。

## CI、依赖与发布

[准确 CI 41774447299](https://github.com/leng125172-code/whale-deck/actions/runs/41774447299) 的 head SHA 为 b880208，主运行及全部 12 路均 completed/success。CLI 查询遇 EOF 后，通过支持 workflow collections 的连接工具取得主运行及各 job 回执，未把失败查询当成通过。

首次四镜像/两宿主预构建分别失败，Invocation 为 `6d4e4b934cdb43af9d91bb8e4c54550e`、`c33def18331744c6898fec867af99e63`，原日志保留。错误为 NuGet 下载 TLS EOF/超时；实际官方 Grpc.Tools 2.84.0 的 nuspec/nupkg 均 200，固定 SDK 内完整下载 26897357 字节成功，未禁用 TLS、换包或调整凭据。错开下载重试后，镜像 `6f4a1e84ac8b44b6897bbe309e4054f2`、宿主 `d4e001308c2044669fc9e1d03b6d7330` 均退出 0；四镜像实际 ID/标签和隔离无网络 CLI、两宿主 CLI/SHA256SUMS 及 .NET/ASP.NET/PKCS 10.0.13 核对通过。预构建不是安装。

随后仅一次标准 update：`whaledeck-dev-publish-b880208.service`，Invocation `657c3c07df6841cea48d56e07b1c5705`，13:28:45–13:31:36 CST，171 秒。终态 active/exited、MainPID 0、Result success、ExecMainCode 1、ExecMainStatus 0。标准宿主发布安装、冻结镜像构建、正常 Migrator 与三应用替换完成，无宿主重启或自动回滚。

六组件/HTTP 实际来源及两个运行 apphost/DLL device/inode 映射均为 b880208；Agent PID 3598741、维护 PID 3598736。安装 validator 通过 systemd、权限、UDS、受限 helper、运行时与动态入口。11 核心 healthy，8 个数据库平台容器完整身份/StartedAt 保持。生产迁移仍为原 1 条记录、18 CHECK/11 fence/1 guard，发布后原 9 意图保持，不把前向迁移器正常运行冒充新 schema。账本 current=b880208、previous=c020c24、pending=null；7 受保护配置保持，user NOPASSWD 不撤销，Agent 授权未扩大。

维护观察器先就绪，179.922 秒内 90 样本全部 HTTP 200，覆盖实际发布和结束后并回到 healthy/Ready。仅记录采样事实，不推断瞬时零中断，也不覆盖旧失败窗口。

## 取消与重载真实分项

探针源码 a99348e（承接 3f4fd2d 的取消单项/诊断）仅运行 `--cancel-only`，不重做七类已成功的 ACL 写入。它创建新的受控四对象树，用实际 Authentik 登录和 Vue 控件生成预检，核对原 key/id 的重载恢复；320×600 暗色下先“继续等待”而不取消，再明确请求取消。

Invocation `f7284ef00f7d4372a32e781095a646f6` 的取消分项为 Canceled：原物理树、全字节/九字段/普通属性与两类 ACL 不变，无 execution、Root 回执或 HDD 新记录。父核验已写出真实逐轮核对信号。诊断保存重载前、重载后及打开文件后的相同 opaque Subject/CID/key/ID；本次重载 `auth/me` 为 200，不是模拟 503，**不能据此认定旧问题由首次身份超时触发或已证明全部竞态消失**。不导出 Cookie/token、完整 storage state 或 URL query。

原管理员正常退出 guard 已返回，四个后续匿名 GET 401；普通用户正常登录、文件页重定向 403/页面私有请求 0、4 GET/4 POST 均 403 后，原脚本在普通用户退出失败。原因是 `/forbidden` 使用独立状态页，没有用户菜单，工具未先点击“首页”。原 unit 退出 1、原 stderr/截图/SQL/回执保持；未执行或重发取消。管理员退出的结构化回执未在中间保存，只能按冻结代码已到达普通用户退出阶段的控制流证明其 guard 已完成，不补造旧 JSON 回执。

工具已修为从 403 状态页点“首页”再正常退出，并即时保存管理员退出/普通拒绝 checkpoint。只读补核原取消记录及物理快照、精确清理这套保留的源目录；没有重建原 CID。原总门禁仍保留失败，恢复性检查不等于原普通上下文已退出。

## 仅认证补验与独立核对

新增 `validate_recursive_acl_auth_only.py/.mjs`，源码冻结 `f8e9f5ae84665b81744ff3f8686e17306badeb87`；Invocation `802ea4366e814a4781ad572a39a1efb7` 成功。它只创建两个新临时身份，正常可见登录/退出，不创建容器、预检或执行任务。新管理员恢复列表 200/no-store、访问原 owner 接口 404；普通用户文件页 403、页面私有请求 0、4 GET/4 POST 全 403，点“首页”后从原用户菜单退出。每个新上下文均观察正常退出 POST 302/303、OIDC end-session 导航、BFF 401、身份端 401/403 与最终登录页；各自四个匿名 GET 401。两组补验上下文不是已关闭的原取消上下文，结果明确标记 originalCancelContext=false。

补验前后原取消 SQL/Job/outbox/audit/event 快照完全相同，新增意图和文件修改均 0；两个身份准确删除。独立只读过程重新核对取消原件/快照、诊断引用、冻结工具依赖和新认证回执，确认原总门禁仍失败。精确清理后不声称重新读取已删除源字节。

实际生产意图现 10 条：8 Succeeded、1 无执行 Expired、1 无执行 Canceled；实际执行仍 8 个，活动 0。普通 HDD 153/递归 HDD 98 的全部原字节和九字段从发布前至补验后保持，旧根记录不删除，未产生 ACL 历史新记录。实际新取消只增加规划意图，不以取消后的自然过期或后台任务状态代替取消回执。

83 项相关离线边界、Python/Node 语法和 8 范围检查通过；本批未改产品前后端或 Agent 协议，1170 前端/41 测试接口浏览器属于此前 auth 候选证据，不能说本轮又跑了新实机全量。工具新增允许固定取消单项，防止重放七类写入；新工具 CI 未冒称终态成功。

私有证据：`deploy/artifacts/acl-auth-release-b880208-551c34d0/` 保存全部准确构建/发布 unit、CI、预构建/安装/运行前后及观察资料；`deploy/artifacts/container-recursive-acl-browser-65bc5af402774fc4b1062db2dc2f5355/` 保存原失败、取消/物理/引用原件、只读恢复、新认证-only 及独立分项核验。两次预构建失败、原取消整体失败和独立补验成功分别归档，未改写历史。八个自有已结束 unit 取得准确 Invocation/退出码后才清理进程为空的残留，不停止生产 Agent/维护入口。

下一批推进递归 ACL 的进行中取消、实时撤权、Worker/Agent 重启/租约、Partial/Unknown/跨写入与磁盘路径故障；旧跨场景引用丢失因果仍保留诊断边界，不因此重做七类文件写入。递归 ACL 历史恢复、可恢复删除/长期清理、维护连续性和其余 59 项仍待；单次完整八场景并未签收，完整 ACL/P1/P2 不升完成。
