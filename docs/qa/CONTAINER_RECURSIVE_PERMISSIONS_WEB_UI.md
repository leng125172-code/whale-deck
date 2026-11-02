# 递归普通权限：管理员API、Vue候选与验证边界

> 2026-11-02后续：0dede08已正常覆盖，真实Authentik→Vue→SQL/Worker→实际树/HDD常规6场景及独立准确清理通过；同设备元数据，不计异盘灾备。进行中故障/业务Partial/Unknown、递归所有者/ACL/删除等继续。见[最新实机证据](CONTAINER_RECURSIVE_PERMISSIONS_REAL_UI.md)。下方未部署/未接业务等为原批次事实。

2026-11-02。源码候选`2056191`，验收器修正`f706c86`，尚未部署；当前宿主e668dbe/Agent1.49、API/Worker/Gateway d1fcca8保持。59主项继续部分实现，17移出不算完成；没有交付master，不自动回滚dev。

## 实现

- 管理员专用异步queue/status/by-key/confirm/cancel及当前管理员Partial/Unknown恢复清单、原任务查询和明确处置；NoStore/64KiB与规范原ID幂等键。Controller仅处理传输，复用已实现的实时权威管理员/原登录校验、不可变树计划、三层HMAC及永久仓储/专用Worker；HTTP不扫描目录树或修改权限。
- fileMode/directoryMode分别显式填写四位普通八进制；queue与确认/处置体均拒绝缺失/重复/多余/大小写别名/类型强转字段，version必须显式非负整数。支持0000普通模式，不接受特殊位/ACL/所有者混入。错误保持固定码，不回显私有输入。
- Vue只在普通目录提供递归入口；冻结原容器/实例/路径/版本及两类模式，异步整树预检后明确确认。原引用刷新/丢回复接续、有限同键重试、六秒查询、隐藏页暂停、到期/实例变化拒绝确认、取消二次确认、401/403/身份/卸载清理、写入互锁。sessionStorage仅保存主体/容器/原键/原ID，不保存路径/模式/确认令牌或凭据。
- 紧凑计划卡显示总项/文件/目录（含根）/预计变更及根属主与执行身份；实际回执显示已完成核对/实际修改/中止/未开始/待证明，实际修改为已核对子集。HDD历史证明与最近可用性观测区分，不冒称实时检查。
- Partial与Unknown均保持原任务和保护、不自动轮询续做/回滚、不提供通用重试，当前管理员可明确处置；原Root状态/实际计数/摘要/历史保留，结束为失败而非整树成功。ACK丢响应只查询原处置，明确核实前不重发。Frontend校验命令、计划、计数和跨版本事实，拒绝替换原确认、回退或抹除实际计数。

## 已执行和待完成

当前后端3794 Unit/308 Integration通过（7默认原生依赖跳过）、Release全方案0警告错误；新增49项包含严格输入和实际loopback MVC/Cookie/CSRF/权限/原登录幂等/实时撤权/私有响应。loopback的身份、SQL仓储与Agent为明确测试夹具，不是实际Authentik/PG/文件验收。前一批真实持久化/双库门禁保持[原批次证据](CONTAINER_RECURSIVE_PERMISSIONS_PERSISTENCE.md)，本批没有重跑或覆盖该证据。

前端832 Unit（含77新契约检查）通过，最终lint/格式/类型/生产构建均通过；最后过期提示与编辑互锁修复已纳入最终生产构建。生产构建+测试API首轮41例为37通过/4失败（1045/1440明暗确认按钮底部830px超过800px视口）。改用Element Plus align-center与有界正文滚动后，完整41例通过，源/构建摘要校验一致；这仍是前一候选，不能单独作为后来两处复核修复的最终签收，最终分批结果见下文。

扩展为44递归例+104关联回归的148例运行，在只读复核发现过期提示误用CONTAINER_OWNERSHIP_PLAN_EXPIRED以及编辑按钮未显式禁用后，准确中断；该轮不记完整通过。已改为递归代码、补普通权限/所有者/递归编辑禁用及对应断言，修复后已重新构建并纳入回归，相关断言均已通过。没有修改产品屏障/Root协议或用模拟完成替代此缺口。

默认Corepack缓存缺包及前端命令首次工作目录错误，在真正测试前改用项目固定pnpm12.10.1与web目录；未升级依赖。77新单测首轮75通过/2失败，原因是测试将两个原为0的计数仍赋0，未实际改变；改为原计数+1后77通过。lint首轮缺少toThrow预期消息，补明确消息后通过；失败日志保留。

截图脚本派生时前缀未改，6张目录临时图曾与所有者目录图同名；已改为独立container-recursive-permission目录，所有者回归改独立recursive-permission-regressions目录。原所有者manifest引用的两张file图最后修改为2026-10-31，早于本批，当前SHA与当时manifest不匹配；本批未写它们、不改写旧manifest、不把它们当原SHA证据。真实实机证据私有目录及旧QA正文未改。首轮4失败截图/上下文已另存recursive-first-failure目录。

## 验收边界与下一步

最终148例联合回归为147通过/1测试断言失败：唯一失败为新增401场景对已清空的sessionStorage(null)使用字符串not.toContain。产品退出及清理已正确，f706c86改为401明确toBeNull后，完整递归44例重新执行44/44通过。原联合回归的104个关联场景全部通过；最终记录为**44新+104关联分批通过**，不冒称单次148全绿。两个批次的产品源码与170个构建文件摘要逐一比较不变，仅验收器的null断言改变。

148分组包括容器浏览/上传、文件页、会话退出、普通权限、所有者及递归。递归44例包含320/1045/1440明暗、320×600取消按钮、明确分开模式与0000/0777、原ID丢回复/确认/刷新、六秒轮询与隐藏页、实例变化/过期、401/403/404/503、取消、两类未决结果不续做、16组Partial/Unknown处置与丢响应、计数/历史保留、已验证历史但当前HDD不可用、互锁及不允许文件入口。全部为生产构建+受控测试接口，不是真实身份/文件/HDD。

当前尚未覆盖应用或迁入生产schema；没有真实Authentik→Vue→SQL/Worker→Agent→实际目录树/HDD业务整链，不计完整递归权限完成。需正常覆盖、真实管理员/普通用户/CSRF/Origin/退出、文件/目录模式与完整计数/HDD原件、Partial/Unknown当前管理员处置及进行中故障矩阵，首次登记未到达服务器且刷新后缺少本地冻结命令等长期解除边界仍需实际故障闭环；当前保留原引用/只查原键，不用前端清除掩盖。之后继续递归所有者、ACL、可恢复删除/长期清理与其余59项/UI/C#酒馆/统一供应商/AI Live2D。

## 最终证据和交付边界

320暗色与1440明色已目视复核：两类模式独立、计划计数与根属主/执行身份分开、正文可滚动、确认与取消完整可见。截图位于`deploy/artifacts/ui-candidates/container-recursive-permission/`，最终manifest为`recursive-final-passed-manifest.json`，保存SourceCommit=f706c86、44/104分批事实、1288份源码与170份构建摘要、两张截图SHA及realAuthentik/realDiskAcceptance=false。预览包在源码提交前从工作树构建，不能当作冻结Git部署或版本来源证据；覆盖发布须重新冻结构建。

日志`/tmp/wd-recursive-ui-{api-loopback,backend-unit,backend-final-build,backend-final-integration,unit-final-all,lint-final-f706,format-final-f706,build-final,browser-reviewed-final,browser-44-final,scope}.log`保留。首轮UI布局失败、已通过旧41例、中断轮、148轮的401断言失败及其截图均保留，未改记通过。CI增加所有者及递归页面专项；本地通过不代表远程CI终态已签收。11核心当前healthy，两宿主active，sudo -n true通过；本轮未重部署、未改sudo或Agent授权、未迁生产schema。

项目图谱前两次阶段写入与Sites连接检查遇传输错误；本地pending保留后按同一eventId重试，阶段回执duplicate=false，项目概述更新亦成功，两个记录已移synced。阶段仍active，不表示递归权限或59项目标整体完成。
