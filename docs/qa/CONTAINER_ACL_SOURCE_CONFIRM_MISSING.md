# ACL恢复：Ready后来源丢失，原确认拒绝的实机验收

2026-11-04，产品仍为28062da/Agent协议1.52，本轮无部署、服务重启、Worker启停、SQL/时钟/缓存修改或会话注入。只签收**Ready后选定历史来源在HDD规范位置缺失，原Vue确认被拒绝**这一组合；不扩签首次登记、异步预检、首次Apply、来源替换或整个HDD不可用。

## 实际运行与回执

门禁源码冻结为`c3dca1f3e0058c45ce91819682ae00b59025d67c`，干净工作树启动，入口清单42文件、13个安装态helper与产品28062da逐字比对。运行后补充Python静态本地导入闭包64文件与同一Git版本逐字核对；这是事后的补充一致性检查，不能把入口清单说成已列全64文件，也不能替代运行前的干净树约束。

```sh
sudo -n python3 -u deploy/validation/validate_container_acl_source_missing.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n python3 -u deploy/validation/verify_container_acl_source_missing.py container-acl-source-missing-71d49e864aa44910b16f66321f313365
```

两条命令均**退出0**。实际证据目录`deploy/artifacts/container-acl-source-missing-71d49e864aa44910b16f66321f313365/`，root:root/0700、原始文件0600，不入Git。真实恢复意图`ef0dd259-2197-6f0a-36ef-62d506ac8116`，实际新捕获`35107a08c15d600060675ad6c8d43489`。

## 真实页面与拒绝事实

1. 新建一个无网络、非root执行、只读根目录的独占测试容器及临时管理员，经正常Authentik可见登录进入Vue。页面先完成一次普通access ACL修改，以产生**新的**合法历史捕获；实际完整文件、SQL、Root Completed及HDD摘要核对后，才放行页面历史扫描、选源和恢复预检。新捕获确实修改了测试目标ACL，不能称整场零文件修改。
2. 原恢复预检Ready且没有执行ID时核实全局仅本案活跃。使用[已验证的隔离工具](CONTAINER_ACL_SOURCE_QUARANTINE_CANDIDATE.md)，只把本轮新捕获元数据移动至同HDD内有inode证明的私有目录；不伪造字节、不删除元数据、不改旧137份记录，不改变目标ACL。
3. 原登录从真实HTTP历史接口读取该准确capture/digest返回**404**；同原幂等键GET仍**200**，返回原意图和冻结来源，原ID、版本与期限保持。该原键只读不依赖来源当前可读，不能被当作“来源仍可恢复”证明。
4. 原320×600暗色Vue点击一次“确认ACL历史恢复”，真实API→签名Agent读取链因来源缺失返回**409 / OPERATION_CONFLICT**。页面显示无法核实/查询原任务/未自动重试确认，确认按钮禁用，完整错误位于视口、无水平溢出；截图`source-confirm-rejected-mobile-dark.png`已人工复核。来源丢失不是502/503重试流程。
5. 拒绝后的完整SQL快照与Ready快照完全相等：仍Ready，执行Job/执行ID/RootStarted/回执/当前ACL前像HDD均无，确认审计0。原预检Job已成功不等于恢复成功，不能把null的执行事实表述成已执行后的“未修改”。同一时点实际目标完整字节、九字段、两类ACL及普通属性与捕获后基线完全相等。
6. 按持久日志和原隔离目录/元数据inode精确还原来源；实际HTTP再读取**200**且完整快照相同。页面仅查询原任务，先“继续等待”验证取消POST为0，再明确取消一次；原任务Canceled、执行ID仍空，来源保持原选定快照，未再次确认。`source-original-canceled-mobile-dark.png`已人工复核。真实请求事件恰为`restore-registration → restore-confirm → restore-cancel`，各1次；内容PUT为0。
7. 原浏览器上下文通过用户菜单正常BFF/Authentik双端退出，BFF401、提供方401/403。另一个新普通身份正常HTTP登录对原恢复及准确历史读取均403，匿名两路401，普通身份正常双端退出；这是HTTP权限证据，未新增普通身份Vue拒绝页面验收。

## 实盘、历史和清理

- 原137份HDD元数据的完整字节SHA256和九字段全部相等。新增1份捕获，规范目录总138；未产生恢复执行的当前ACL前像HDD，原Root Completed及其字节保持。
- 新捕获原件还原后，字节、device/inode、mode、uid/gid、link count、size、mtime ns保持，ctime从`1793734819938484856`变为`1793734843318379092`。移动必然改变本轮原件及目录时间戳，不声称本轮新捕获九字段全不变。私有隔离目录已准确移除，原元数据永久保留。
- 原恢复Ready→取消的身份、命令/计划/来源JSON及哈希、原期限不变，仅正常取消状态/版本/时间字段改变。3条原主体恢复审计分别plan-accepted、plan-result Ready、cancel-requested；没有confirmed或执行result审计，没有为确认失败伪造执行事件。
- 独立核验重新读取实际SQL、Root、HDD及Git对象，核实原Ready/拒绝/取消快照、正常退出、两PNG及权限回执、原件精确还原、准确源/容器及全部临时身份已清理。源文件在清理前由父门禁实际核实；独立核验不声称重新读取已经删除的源字节。
- 六组件仍28062da，Agent PID2178169、MaintenanceHost PID2178164，11核心容器ID/镜像/StartedAt保持且healthy，7项配置及完整发布账本保持。任务、租约、屏障、终端及路径锁均归零。旧失败源树未清理，未扩大产品Agent授权，保留开发user NOPASSWD。

本轮离线5项新证明、22项隔离/强杀恢复、28项旧恢复证明和8项范围检查通过，0跳过，Python/Node语法及文档链接检查通过；不是全量前后端重跑。e9a1ced前批CI五个基础job成功、六浏览器分片仍进行，新提交的CI终态另取，不冒称全绿。

## 仍待完成

本场常规首次捕获/计划和Ready后来源缺失确认拒绝首次执行通过。后续使用全新资源分别完成首次登记、异步预检、首次Apply前的丢失/替换，以及临时依赖不可用续做、原期限、已有三态回执优先等[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。递归ACL/恢复、可恢复删除与长期清理、P0-05维护发布连续性及其余59项仍未完成，主项和六阶段不因此整项升完成。
