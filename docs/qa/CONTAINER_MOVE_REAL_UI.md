# a59ec97容器移动：真实身份、业务、Root与页面常规整链

2026-10-29，运行版本 `a59ec973478471745a1b7a5aa41da16dae20d426`。b93cd27验证器使用真实Authentik登录、实际BFF/SQL/Worker/签名UDS/helper、独占Docker测试容器及实际磁盘；未拦截响应、注入登录态或用页面fixture替代服务。常规子门禁通过；进行中取消/撤权、重启、Unknown真实故障等仍待，不签完整P1-11/P2-03或59项。

## 已签收范围

- 正常用户名/密码登录真实Authentik管理员；四组移动分别为1440明色文件、1440暗色目录、320明色目录、320暗色文件。每组使用独立源，完整2097159字节文件或包含该文件与空目录的真实树。
- 页面返回新表单不登记；后台完成预检后才确认。原登记HTTP精确重试4次仍返回原意图，四次确认各生成一个独立执行Job；执行存在时刷新页面只查询原引用，不新增任务/再次确认。页面没有PUT文件内容请求。
- 页面最终成功、原源路径消失、新目标显示；独立Python父进程在删除测试数据前核对实际SQL双任务/Outbox/审计、Root永久内外回执、容器版本、完整目标与私有原件全部字节、普通mode/UID/GID/纳秒mtime，以及原文件inode保持。两棵树含混合所有者；私有目录由Root重建，未声称保留原目录inode。
- 发布、源移除、原件留存在结果DTO、SQL和Root均为三true；Root内层规范摘要及实际文件版本一致。两宽明暗预检/完成截图保留；320明色实机截图另行目视确认长路径/确认警告在有界内容区、底部按钮可见。
- 第5个计划在Ready明确取消，取消弹窗先返回继续等待、再明确确认；没有执行Job/RootStarted/目标，三项事实保持null。最终4 Succeeded/1 Canceled，永久任务与审计保留。
- 无CSRF的写入400、伪造Origin403。真实普通用户4个读入口及4个写入口均403；匿名4个读入口401，没有额外目标文件。
- 伪造注销hint被400拒绝且不破坏真实身份会话。正常从另一页退出后两页均回登录页，旧Cookie访问WD401、Authentik用户接口拒绝，移动私有界面清空。
- 独占容器与准确源目录在所有验证通过后清理；永久SQL/Root历史保留。第三个只读进程重读原SQL/原Root/归档回执、源指纹及两类manifest摘要，核对准确容器/源不存在、临时身份0、活动/屏障/租约0、11核心healthy。它不声称删除后又读取了物理字节。

以上实机业务源与目标均在本次HDD绑定挂载；此前已安装固定helper的三次HDD/NVMe跨盘证据独立保留，不冒称本次已验证跨盘BFF/UI。

## 验证器与失败记录

- `validate_container_move_browser.py/.mjs`：为四组实际源分别冻结元数据/版本，完成后独立核验及准确清理；临时身份仅通过既有Authentik API建立并删除，凭据仅内存/私有子进程环境，不进入Git/日志/浏览器存储。
- `verify_container_move_browser.py`：只读复核SQL、Root、确切清理及身份，不创建身份、不写SQL、不操作或删除容器/文件。
- 实际业务和页面首轮通过。只读后置观察器首轮混淆源manifestSha256与带独立Move域的留存manifestDigest；按既有 `whaledeck-tree-move-v1` 域分别重算并保留两类断言后通过，没有修改产品回执或物理数据。
- 实机PNG位于root私有证据目录，普通图像工具无读权限；只为目视核验复制一张明确的移动页面PNG到0600临时文件，未放宽私有证据目录权限。

私有证据：`deploy/artifacts/container-move-browser-9a71d7791d0a47fe9962f18c1ddb447a/` 中baseline/results/4份原Root/verified/independent-final及明暗截图。日志 `/tmp/wd-container-move-real-browser.log`；只读首轮及修正日志 `/tmp/wd-container-move-browser-independent{,-second}.log`。校验后测试数据不存在，当前Root历史Completed是不可变历史，不代表清理后仍有当前物理绑定。

## 继续

保持59项active、17项移出不计完成。补实际进行中取消/退出/撤权、Worker/Agent重启、发布/留存中断与Unknown当前管理员处置、跨盘业务及剩余权限/保留/故障矩阵。旧复制、目录及宿主文件证据继续有效，不以单个正常链路签完整文件模块。

## 后续部分文件中断验收

2026-10-29，真实BFF进行中取消/正常退出、Worker SIGKILL/自然租约及Agent systemd重启四例已通过，详见[部分文件中断证据](CONTAINER_MOVE_INFLIGHT.md)。上文“仍待”保留为当时边界；可见页面进行中交互、目录树发布/源留存/Unknown以及跨盘BFF仍未因此签收。
