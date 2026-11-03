# ACL恢复：首次登记前有效来源替换

2026-11-04，仅新增签收真实页面选源后、首次登记前来源被同captureId的另一有效记录替换。profile7/source-register-replaced；产品仍28062da/协议1.52，无部署或Worker/Agent/维护宿主启停，无SQL/缓存/系统时钟修改。已有缺失、Ready确认和首次Apply替换证据保持，不重放。

## 冻结和实际结果

实际门禁与独立核验冻结`c0245ed2dbd1a289a5411a2db893509bbd57e4b5`，干净树、运行前97文件Python/MJS闭包及13安装helper逐字核对通过。以下实际命令均退出0，独立核验不重做捕获、替换、登记或清理操作：

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/validate_container_acl_source_register_replaced.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/verify_container_acl_source_register_replaced.py container-acl-source-register-replaced-498fe40b1816472f8bfee588fcaed81b
```

私有证据目录`deploy/artifacts/container-acl-source-register-replaced-498fe40b1816472f8bfee588fcaed81b/`为root:root/0700、文件0600，不入Git。新普通捕获为`c4a40b3a51e6552b39c2a36634257c67`；按原Subject/登录绑定/幂等键**推导预期**恢复意图`e370719f-973f-4da3-b637-30356944bf54`、执行`57eff2d3-1d93-44eb-497f-ce67dcfe3fbe`，二者始终不存在，不冒充实际Job ID。

1. 新无网络/非root/只读根容器、独占目录和临时管理员，正常Authentik→Vue完成一次普通access ACL修改产生新来源。父门禁实际验证2097159字节/九字段/两ACL/普通属性及SQL/Root Completed/HDD后，页面正常选中此来源，尚未POST恢复。普通捕获确实修改了测试目标ACL，后续保持均相对此捕获后基线，不称整场零修改。
2. 仅使用[已验证的准确替换工具](CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)：原件保留在准确隔离inode，另一临时inode仅createdAtUnixMs+1并重算正确规范digest，完整写入/fsync后NOREPLACE发布。旧摘要实际409/AGENT_VERSION_CONFLICT，新摘要实际200，完整响应仅时间/摘要不同，证明是有效自洽记录，不是缺文件或畸形JSON。
3. 原页面唯一“生成ACL恢复预检”POST实际仍携带原captureId/摘要、instance/path及当前目标version，返回409/OPERATION_CONFLICT；同key查询404。原Subject/登录绑定/幂等键推导的意图、Job、Outbox、事件、恢复审计五项SQL整数计数均0。预期Root计划/执行记录及当前恢复HDD前像缺席，目标完整物理清单和普通捕获原Root字节保持。新摘要可读不等于新业务采用它。
4. 撤回准确QA替换inode、归档其字节后清理，仅还原原来源inode；按原摘要读取200、完整原快照恢复。页面仅查询原登记，仍404；两个预期Job HTTP均404，SQL五项仍0、Root/HDD仍缺席。可见重试按钮未点击，不自动刷新来源或重新提交，无Ready/Pending/确认/Apply，也不伪造Failed任务。

## 页面、权限和原件

320×600暗色登记拒绝弹窗完整显示无法核实/查询原任务/不会自动重新提交，随后320暗色和1440亮色均显示尚未找到原登记，三个实际PNG均已人工查看；原引用、查询/显式重试按钮和错误在页面可见，无水平溢出。真实请求登记1、确认0、取消0、内容PUT0、Job写0，恢复审计0。原上下文正常用户菜单BFF/Authentik双端退出，sessionStorage原引用键null；另一个新普通身份正常HTTP登录两路403、匿名两路401，普通身份正常双端退出，不冒称新增普通身份Vue拒绝页面。

旧143份HDD元数据全部字节及九字段保持，规范目录总144，仅新增普通捕获1份；该捕获Root/HDD永久保留，没有恢复任务或执行前像。QA替换先归档后准确清理，不计新历史、不当产品删除能力验收。新原件字节及前8字段保持，ctime从1793741328299380875变为1793741346099774087；目录时间随移动变化，不称新原件九字段全不变。源与历史同device，不算异盘恢复或整机NVMe/HDD布局验收。

独立只读核验实际SQL五项0、原捕获Root/HDD/替换归档、准确原件恢复、请求与退出/页面证据、预期Root/HDD缺席及清理。父脚本和独立验证器各自使用source-register-replaced生成规则且核对SHA；已清理源的完整字节由父门禁清理前实际检查，独立核验不声称重读已删除源。源/容器/临时身份准确清理，旧失败树未动，活动任务/租约/屏障/终端/路径锁归零。

11核心healthy且全部ID/镜像/StartedAt保持，Agent2178169/维护2178164及实际28062da来源保持；7项受保护配置与完整发布账本保持，NOPASSWD授权保留，产品最小权限不扩大。root只读Git禁用可选锁，实机/独立检查前后用户索引所有者1000:1000、inode/模式/字节SHA保持。

## 后续工具补强与尚未签收

实机成功后只读复核发现两处**工具异常收尾**缺口：非预期POST响应可能在推导/查账前中断；替换持久日志写成前的残留stage可能未标记。9202339补强为先保存请求引用，再核拒绝；异常时用原Subject/登录绑定/key推导查账，发现可能登记则明确记录未解决并保留源，不擅自取消或删除。尚未写成持久日志的stage保留、写待恢复标记，不把未证明对象删除。4个新增离线检查（含两个实际隔离文件系统检查）验证原请求绑定及残留保留/告警；这是后续修复候选，**未实机注入这些异常，也未重放已完成场景**，本场不存在registration-attempt.json不补造。

最终154相关离线检查以root执行通过、0跳过：12本场专项、16替换工具、22隔离、其余各阶段来源/旧恢复证明和8范围。严格Python/Node语法通过。CI只接入离线门禁；前批c9baf14/41721278529本轮最后查询仍in_progress、4项基础成功，安装/6浏览器分片待，新提交终态另取；不称CI全绿或全量前后端重跑。

本场只签首次登记前这一种有效替换。异步预检前替换、其它内容变化、依赖暂不可用/原期限/原三态回执、递归ACL/恢复、可恢复删除/长期清理与P0-05维护发布连续性及其余59项仍待。当前59主项和阶段不升完成，不自动回滚dev。[完整来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。
