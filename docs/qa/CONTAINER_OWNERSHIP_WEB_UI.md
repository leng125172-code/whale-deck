# 容器所有者：管理员 API 与 Vue 候选验证

> 2026-11-02证据核对：本文两张file候选PNG最后修改于2026-10-31 10:26，晚于同日08:48的container-ownership-manifest.json，当前SHA与该旧manifest不匹配；这两图不再作为原SHA签收依据，未改写旧manifest或下方原始记录。当前递归权限候选的所有者兼容回归使用独立recursive-permission-regressions截图目录，不冒称重现旧源码/真实身份。详见[新批次与证据边界](CONTAINER_RECURSIVE_PERMISSIONS_WEB_UI.md)。

> 2026-10-31后续：d5e62ca已覆盖，真实身份/文件/HDD常规页面7场景及独立复核通过，见[实机证据](CONTAINER_OWNERSHIP_REAL_UI.md)。下方未部署/API/UI待接等保留为原批次事实；所有者进行中故障/Unknown、递归/ACL/删除与长期清理仍待。

日期：2026-10-31。基于 `6134e22` 的开发候选；永久 SQL/专用 Worker 的原生证据见[前批记录](CONTAINER_OWNERSHIP_PERSISTENCE.md)。**本批未部署，也没有真实 Authentik → Vue → SQL/Worker → Agent → 实际文件/HDD 的整链验收。** 下述浏览器使用本地生产构建预览和明确测试接口，不能替代实机验收。

## 已实现的候选

- 独立管理员所有者 API：预检登记、原 ID/原幂等键查询、原任务确认/取消，以及当前管理员 Unknown 列表、查询和明确处置。双控制器禁缓存并限制请求体 65536 字节，业务复用原所有者用例；未增加 Root RPC 或放宽产品 Agent 权限。
- 目标 UID/GID 必须明确提供，0 是显式合法值，最大支持 4294967294；空字段/null、负数、字符串、浮点/科学计数、4294967295 哨兵、重复/额外字段与大小写别名均拒绝。仅接受 instanceId/path/uid/gid/expectedVersion 五字段；缺字段不能默认改为 root。无效模型状态返回有界稳定错误码及 traceId，不回显私有输入。
- Vue 分离原 inode UID/GID、目标 UID/GID 与内核执行 FSUID/FSGID，展示原计划后再明确确认；无变化依据原 inode 判断。原确认丢回复或刷新只查询原引用，不创建替代写入。过期/实例变化/未知响应禁旧确认；403 清理身份提示，404/503 不冒称空结果或成功。
- 原所有者任务与既有上传/编辑/历史恢复/目录/复制/移动/普通权限入口互锁；提示仅保存主体、容器、原键/ID（最多4项），不存路径、确认签名、凭据或 UID/GID。失效、退出及角色变化清理，隐藏页面暂停轮询，恢复可见后只查询原任务。
- 当前管理员 Unknown 处置保持原 UID/GID 和 HDD 不确定事实，明确确认后调用原意图的封存流程；回复丢失先查原状态，不重复处置或伪造旧主体成功。尚未宣称完整所有者、递归/ACL 或删除功能完成。

## 失败发现与修复

320×800 首次目视检查发现预检内容较长时取消按钮落在视口下方，原自动检查只约束确认按钮，未发现该问题。修复为所有者弹窗使用有界动态视口高度、正文内部滚动、固定保留头部与底部；小屏上下留12px。现检查同时约束确认与取消按钮，未通过缩小文字掩盖问题。

修复后原31项浏览器完整通过；再增加320×600明暗两项，确认取消按钮完整位于视口内，先“继续等待”零取消、再明确确认仅向原 ID/幂等键发送一次取消，且零执行确认。过期计划仍显示旧“请确认”说明的问题也已修正，保留原诊断事实但结果提示明确过期。测试截图曾被下一次 Playwright 清空输出目录；改存忽略的独立候选目录后重新生成，不将缺失截图报作证据。

复核还发现更高版本的 Ready 响应可能替换原确认签名或提示；现对 Ready→Ready 保留原确认签名/期限/提示的不可变校验。新增三项单元分别验证签名、期限、提示被改写时拒绝（相同原计划的版本推进仍允许），并增加页面查询坏签名后明确报错、禁止执行确认的回归。该客户端校验补强不替代服务端原有签名与版本验证。最终完整34项浏览器重跑通过，新增坏签名查询后禁确认用例包含在内。

## 实际执行结果

| 验证 | 实际结果 | 证据/边界 |
| --- | --- | --- |
| .NET Release 全方案 | 0 warnings / 0 errors | `/tmp/wd-owner-ui-final-backend-build.log` |
| 后端单元 | 3581 passed / 0 skipped | `/tmp/wd-ownership-http-final-all.log` |
| 集成 | 247 passed / 7 默认原生 skipped | 同上；含所有者传输/模型错误等检查，不是实际 BFF 登录 |
| 前端单元 | 52 files / 755 passed | `/tmp/wd-ownership-unit-final-755.log` |
| 所有者最终生产预览浏览器 | 34 passed | `/tmp/wd-ownership-browser-final-34.log`；320/1045/1440、明暗、文件/目录、320×600取消、原确认不可改写及故障状态 |
| 前置320×600取消专项（已包含于最终34） | 2 passed | `/tmp/wd-ownership-short-screen.log`；明暗均通过，不与最终34项重复累计 |
| 普通权限/复制/移动/会话相关浏览器 | 89 passed | `/tmp/wd-ownership-related-browser.log`；明确测试接口 |
| Vue 类型/生产构建 | passed | `/tmp/wd-ownership-build-final.log` |
| 所有者传输专项复核 | 33 passed / 0 skipped | `/tmp/wd-ownership-http-recovery-final.log`；包含于前述247，不重复累计 |
| 59项范围基线 | 8 passed | `python3 -m unittest discover -s deploy/validation -p test_scope_baseline.py -v`；仅证明范围记账 |
| lint / src 格式 | passed | `/tmp/wd-ownership-lint-final.log`、`/tmp/wd-ownership-format-final.log` |

浏览器入口：`PLAYWRIGHT_EXECUTABLE_PATH=/usr/bin/google-chrome WHALEDECK_PRODUCTION_PREVIEW_URL=http://127.0.0.1:5175 pnpm --dir web exec playwright test e2e/container-ownerships.spec.ts`。修复后先通过31项，再以 `--grep '320x600'` 验证两项，最终确认不可改写修复后完整34项全部重跑通过；不重复累计。

实际目视复核320暗色与1440明色候选截图；小屏修复后再次复核320暗色，四个底部动作包含取消均可见。截图位于忽略目录 `deploy/artifacts/ui-candidates/container-ownership-320-dark-file.png` 和 `container-ownership-1440-light-file.png`，使用合成文件名/数据，不提交到仓库，也不作为真实工作站页面证据。两张截图的SHA256及夹具边界另存于同目录 `container-ownership-manifest.json`。

## 后续真实验收门槛

冻结候选并按既有 dev 流程覆盖发布（不自动回滚），核对 API/Worker/Gateway 与宿主来源、生产迁移、匿名/普通用户/管理员、CSRF/Origin、真实 Authentik 登录/退出、UID/GID=0及普通值、文件/目录与无变化/恢复原属主。独立核对 SQL 双任务/审计、Root 原回执、原 inode/完整内容/模式/mtime/子项及物理 HDD 元数据；补取消/退出/进程重启/Unknown 当前管理员处置与准确清理。前批真实 PG 及 Agent RPC 证据仍独立有效，但不拼接冒称本批已经贯通。之后继续递归/ACL、可恢复删除、长期清理与全部59项。

本批修改的5份MD共495个本地链接均存在，`git diff --check`通过。原59项/17项移出及既有历史证据保持；源码、文档通过不等于部署或真实业务验收。
