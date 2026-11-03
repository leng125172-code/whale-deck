# ACL恢复：首次登记前来源丢失、零登记实机验收

2026-11-04，本场签收**真实页面先选中有效新来源，首次登记POST前该准确来源丢失**。与[异步预检](CONTAINER_ACL_SOURCE_PLAN_MISSING.md)、[Ready后确认](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)、[首次Apply](CONTAINER_ACL_SOURCE_APPLY_MISSING.md)分开记录，不重做前批操作；四场缺失来源分项通过不等于来源替换/依赖中断或完整ACL完成。运行仍28062da/协议1.52，本场无发布、Worker启停或两宿主重启。

## 实际执行

门禁冻结`1537d6a038366dd3441af2010cf51aa4c3b4e3c1`，干净树启动，运行前本地Python/MJS静态导入闭包79文件及13个安装helper逐字核对。新profile只复用普通捕获和真实历史选择，到恢复弹窗后返回，首次POST由独立门禁控制；既有成功登记路径保持。

```sh
sudo -n python3 -u deploy/validation/validate_container_acl_source_register_missing.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n python3 -u deploy/validation/verify_container_acl_source_register_missing.py container-acl-source-register-missing-ae12b4bf60b5485b9ef95f0836a682fa
```

实机与独立核验均**退出0**。私有证据`deploy/artifacts/container-acl-source-register-missing-ae12b4bf60b5485b9ef95f0836a682fa/`为root:root/0700、文件0600，不入Git。新普通捕获`9c3492144c26d6f2ed62579f5f576d18`。

**没有服务端恢复意图或执行ID。** `f871ef30-4c2e-516f-a239-fa95f6c2c947`和`0e05e301-d2c1-274d-0e7c-556ec2159676`仅为按原subject/login/幂等键推导、用于反查的预期意图/执行标识，不能写成已创建任务。推导代码先与本轮普通捕获的实际SQL行及编译产品生成ID相等核对，独立核验再次检查，避免错误ID的404产生假“未登记”证据；login绑定只在私有原始证据中使用。

## 真实拒绝与恢复

1. 新无网络/非root/只读根测试容器、独占目录和临时管理员，正常Authentik→Vue完成一次普通access ACL捕获。父门禁实际验证完整2097159字节、身份/两类ACL/属性、SQL/Root Completed/HDD后才放行真实历史扫描、读取和选源。普通捕获确实修改测试目标ACL，后续保持均相对于捕获后基线，不能说整场零修改。
2. 页面打开来源正确的恢复弹窗，尚未提交恢复登记，核实全局无其它活动任务/终端/文件工作。仅隔离本轮新捕获的准确HDD规范元数据，保留同inode和私有持久日志/目录证明；实际历史读取**404**。旧140份记录和当前目标不改。
3. 原Vue仅点击一次“生成ACL恢复预检”，实际API→签名Agent读源链返回**409 / OPERATION_CONFLICT**。请求五字段、准确instance/path/capture/digest与实际目标当前版本均核对；不是错误参数或权限403造成的拒绝。页面完整显示无法核实/查询原任务/不会自动重提，确认/取消控件不存在；保留显式“重试原预检登记”入口但**未点击**。
4. 原登录同键GET返回**404**。真实SQL五项计数均为严格整数0：该主体恢复意图及预期意图、原键/派生ID Job、相应outbox、Job事件、该主体恢复审计。预期意图/执行ID对应的Root/HDD记录也均不存在。不伪造Failed或Canceled任务、执行回执、任务摘要或完成审计。
5. 原件按同inode精确还原，实际历史再读**200**且完整快照相同。原页面只查询同一原键，仍404，两个预期Job GET也均404；SQL五项仍0、Root/HDD仍缺席、目标完全保持。来源恢复不使未登记请求自动创建任务，没有第二次POST，也没有手动重试登记。
6. 320×600暗色原拒绝、320×600暗色原键未找到、1440×600亮色原键未找到，共3张真实PNG，全部人工复核。错误提示完整进入视口、无水平溢出；没有为不存在任务制作任务详情。原上下文通过用户菜单正常BFF/Authentik双端退出，BFF401/提供方401或403，sessionStorage原ACL引用键已清空。

恢复登记POST1，恢复确认/取消/内容PUT/通用Job写入均0；仅一次普通捕获的合法确认不计为恢复确认。另一个新普通身份正常HTTP登录，对预期恢复状态和准确历史读取两路403，匿名两路401，普通身份正常双端退出；不冒称新增普通身份Vue拒绝页面。其权限拒绝也不等于恢复任务曾存在。

## 原件、运行、清理

- 旧140份HDD元数据全部字节和九字段保持；总141，只新增普通捕获1份。恢复登记为0，没有恢复Root/Discard或当前ACL前像；原普通捕获Root/HDD永久保留。
- 本轮新元数据原件字节、device/inode、mode、uid/gid、link count、size及mtime保持，ctime从`1793738015834043512`变为`1793738033496106805`。移动也改变目录时间，隔离目录已准确移除；不称本轮新原件九字段全不变。QA源/HDD同device，不算异盘恢复或整机NVMe/HDD布局验收。
- 独立核验重新读取实际SQL零计数、Root/HDD缺席、原捕获原件、源码闭包/Git对象、三个真实错误页面证据、正常退出及准确清理。源字节由父门禁在清理前实际核实；独立核验不声称重读已删除源文件。
- 本轮测试源/容器和临时身份已准确清理，旧失败源树未动。任务/租约/屏障/终端/路径锁归零。六组件仍28062da，Agent2178169/维护2178164及全部11核心ID/镜像/StartedAt均保持，11核心healthy；7项受保护配置与完整发布账本保持，开发user NOPASSWD保留，产品Agent授权未扩大。

本轮8项零登记证明、8项异步预检、11项执行/闭包、5项确认缺失、22项隔离/强杀恢复、28项旧恢复证明和8项范围回归通过，0跳过；Python/Node严格语法通过。本场新资源首次执行通过，未重跑全量前后端。前批b0b0fea CI最后只读查询仍in_progress，新提交CI另取终态，不据局部成功签全绿。

## 尚待

[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)中的真实替换、临时依赖不可用续做、原期限和已有三态回执优先等仍待实机；四个缺失来源边界不代表整盘/Agent离线行为。更多跨写入/磁盘组合、递归ACL/恢复、删除长期清理、P0-05维护发布连续性及其余59项继续，主项及六阶段不升整项完成。
